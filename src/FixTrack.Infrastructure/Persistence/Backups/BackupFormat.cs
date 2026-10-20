using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FixTrack.Infrastructure.Persistence.Backups;

/// <summary>Contenido del archivo <c>manifiesto.json</c> de un respaldo.</summary>
/// <param name="Format">Versión del formato del archivo.</param>
/// <param name="Database">Nombre de la base de origen (sin servidor ni credenciales).</param>
/// <param name="Isolation">Aislamiento de la transacción de lectura: todas las tablas corresponden al mismo instante.</param>
/// <param name="Migrations">Migraciones aplicadas en el origen: el esquema exacto que necesitan los datos.</param>
public sealed record BackupManifest(
    int Format,
    DateTime CreatedAtUtc,
    string Database,
    string Isolation,
    IReadOnlyList<string> Migrations,
    IReadOnlyList<BackupTable> Tables)
{
    public const int CurrentFormat = 1;
    public const string EntryName = "manifiesto.json";

    public long TotalRows => Tables.Sum(table => table.Rows);
}

/// <param name="Sha256">Hash del archivo de la tabla dentro del respaldo (filas en orden de clave primaria).</param>
public sealed record BackupTable(string Name, IReadOnlyList<BackupColumn> Columns, long Rows, string Sha256)
{
    public string EntryName => $"tablas/{Name}.jsonl";
}

public sealed record BackupColumn(string Name, string Type, bool IsIdentity);

/// <summary>Una tabla de la base tal como la describe el catálogo de SQL Server.</summary>
internal sealed record CatalogTable(string Name, IReadOnlyList<BackupColumn> Columns, IReadOnlyList<string> PrimaryKey, IReadOnlyList<string> References);

/// <summary>
/// Formato de los datos: una línea JSON por fila con los valores en el orden de las columnas. La representación es
/// determinista (fechas con 7 decimales, decimales en texto exacto, GUID en minúsculas), así que leer la misma tabla
/// dos veces produce los mismos bytes y el mismo hash: así se comprueba que una restauración quedó idéntica.
/// </summary>
internal static class BackupFormat
{
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff";
    private const string DateTimeOffsetFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz";
    private const string MigrationsHistory = "__EFMigrationsHistory";

    private static readonly HashSet<string> SupportedTypes =
    [
        "bit", "tinyint", "smallint", "int", "bigint", "decimal", "numeric",
        "char", "varchar", "nchar", "nvarchar",
        "date", "datetime", "datetime2", "datetimeoffset", "uniqueidentifier", "binary", "varbinary",
    ];

    public static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonWriterOptions RowJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Bracket(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>
    /// Tablas de usuario del esquema <c>dbo</c>, salvo el historial de migraciones (lo recrean las migraciones), en
    /// orden de dependencias: cada tabla aparece después de las que referencia. Lee el catálogo, no el modelo de EF: la
    /// base puede tener un esquema anterior al de esta versión de la aplicación.
    /// </summary>
    public static async Task<IReadOnlyList<CatalogTable>> ReadCatalogAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        var columns = new Dictionary<string, List<BackupColumn>>(StringComparer.Ordinal);
        var unsupported = new List<string>();
        await using (var command = Command(connection, transaction, """
            SELECT t.name, c.name, ty.name, c.is_identity
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.is_ms_shipped = 0 AND t.name <> @history AND c.is_computed = 0
            ORDER BY t.name, c.column_id
            """))
        {
            command.Parameters.AddWithValue("@history", MigrationsHistory);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var column = new BackupColumn(reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
                if (!SupportedTypes.Contains(column.Type))
                {
                    unsupported.Add($"{reader.GetString(0)}.{column.Name} ({column.Type})");
                }

                columns.TryAdd(reader.GetString(0), []);
                columns[reader.GetString(0)].Add(column);
            }
        }

        if (unsupported.Count > 0)
        {
            throw new BackupException($"El respaldo no admite el tipo de estas columnas: {string.Join(", ", unsupported)}.");
        }

        var keys = await PairsAsync(connection, transaction, """
            SELECT t.name, c.name
            FROM sys.tables t
            JOIN sys.indexes i ON i.object_id = t.object_id AND i.is_primary_key = 1
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE t.schema_id = SCHEMA_ID(N'dbo')
            ORDER BY t.name, ic.key_ordinal
            """, cancellationToken);
        var references = await PairsAsync(connection, transaction, """
            SELECT DISTINCT OBJECT_NAME(fk.parent_object_id), OBJECT_NAME(fk.referenced_object_id)
            FROM sys.foreign_keys fk
            WHERE fk.parent_object_id <> fk.referenced_object_id
            """, cancellationToken);

        var tables = columns.Keys.Select(name => new CatalogTable(
            name,
            columns[name],
            [.. keys.Where(pair => pair.First == name).Select(pair => pair.Second)],
            [.. references.Where(pair => pair.First == name && columns.ContainsKey(pair.Second)).Select(pair => pair.Second).Order(StringComparer.Ordinal)]));
        return DependencyOrder(tables);
    }

    /// <summary>Orden topológico estable: entre tablas independientes, el orden alfabético.</summary>
    private static List<CatalogTable> DependencyOrder(IEnumerable<CatalogTable> tables)
    {
        var pending = tables.OrderBy(table => table.Name, StringComparer.Ordinal).ToList();
        var ordered = new List<CatalogTable>();
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(table => table.References.All(reference => ordered.Any(done => done.Name == reference)))
                ?? throw new BackupException("Las claves foráneas forman un ciclo: no hay un orden de carga posible.");
            ordered.Add(next);
            pending.Remove(next);
        }

        return ordered;
    }

    /// <summary>Migraciones aplicadas en orden, o una lista vacía si la base no tiene historial.</summary>
    public static async Task<IReadOnlyList<string>> ReadMigrationsAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            $"IF OBJECT_ID(N'dbo.{MigrationsHistory}', N'U') IS NOT NULL SELECT MigrationId FROM dbo.{Bracket(MigrationsHistory)} ORDER BY MigrationId");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations;
    }

    /// <summary>Escribe las filas de la tabla en <paramref name="output"/> y devuelve cuántas son y el hash de los bytes.</summary>
    public static async Task<(long Rows, string Sha256)> WriteRowsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string table,
        IReadOnlyList<BackupColumn> columns,
        IReadOnlyList<string> primaryKey,
        Stream output,
        CancellationToken cancellationToken)
    {
        var order = primaryKey.Count > 0 ? primaryKey : columns.Select(column => column.Name).ToList();
        await using var command = Command(connection, transaction,
            $"SELECT {string.Join(", ", columns.Select(column => Bracket(column.Name)))} FROM dbo.{Bracket(table)} ORDER BY {string.Join(", ", order.Select(Bracket))}");
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new ArrayBufferWriter<byte>();
        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            buffer.ResetWrittenCount();
            await using (var writer = new Utf8JsonWriter(buffer, RowJson))
            {
                writer.WriteStartArray();
                for (var index = 0; index < columns.Count; index++)
                {
                    WriteValue(writer, columns[index].Type, await reader.IsDBNullAsync(index, cancellationToken) ? null : reader.GetValue(index));
                }

                writer.WriteEndArray();
            }

            buffer.Write("\n"u8);
            hash.AppendData(buffer.WrittenSpan);
            await output.WriteAsync(buffer.WrittenMemory, cancellationToken);
            rows++;
        }

        return (rows, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void WriteValue(Utf8JsonWriter writer, string type, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case byte or short or int or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case decimal number:
                writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case DateTime date:
                writer.WriteStringValue(date.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset date:
                writer.WriteStringValue(date.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture));
                break;
            case Guid id:
                writer.WriteStringValue(id.ToString("D"));
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            default:
                throw new BackupException($"Valor de tipo {type} no admitido en el respaldo.");
        }
    }

    /// <summary>Convierte un valor leído del archivo al tipo .NET de la columna para cargarlo con SqlBulkCopy.</summary>
    public static object ReadValue(JsonElement element, string type)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return DBNull.Value;
        }

        return type switch
        {
            "bit" => element.GetBoolean(),
            "tinyint" => element.GetByte(),
            "smallint" => element.GetInt16(),
            "int" => element.GetInt32(),
            "bigint" => element.GetInt64(),
            "decimal" or "numeric" => decimal.Parse(element.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
            "char" or "varchar" or "nchar" or "nvarchar" => element.GetString()!,
            "date" or "datetime" or "datetime2" => DateTime.ParseExact(element.GetString()!, DateTimeFormat, CultureInfo.InvariantCulture),
            "datetimeoffset" => DateTimeOffset.ParseExact(element.GetString()!, DateTimeOffsetFormat, CultureInfo.InvariantCulture),
            "uniqueidentifier" => Guid.Parse(element.GetString()!),
            "binary" or "varbinary" => element.GetBytesFromBase64(),
            _ => throw new BackupException($"Tipo {type} no admitido en el respaldo."),
        };
    }

    public static Type ClrType(string type) => type switch
    {
        "bit" => typeof(bool),
        "tinyint" => typeof(byte),
        "smallint" => typeof(short),
        "int" => typeof(int),
        "bigint" => typeof(long),
        "decimal" or "numeric" => typeof(decimal),
        "date" or "datetime" or "datetime2" => typeof(DateTime),
        "datetimeoffset" => typeof(DateTimeOffset),
        "uniqueidentifier" => typeof(Guid),
        "binary" or "varbinary" => typeof(byte[]),
        _ => typeof(string),
    };

    public static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 300 };

    private static async Task<List<(string First, string Second)>> PairsAsync(SqlConnection connection, SqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pairs = new List<(string, string)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add((reader.GetString(0), reader.GetString(1)));
        }

        return pairs;
    }
}

/// <summary>Un respaldo no se puede crear, verificar o restaurar; el mensaje explica por qué, sin datos sensibles.</summary>
public sealed class BackupException(string message) : Exception(message);
