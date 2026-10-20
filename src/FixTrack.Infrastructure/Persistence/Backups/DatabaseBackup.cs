using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FixTrack.Infrastructure.Persistence.Backups;

/// <summary>
/// Respaldo lógico de la base, para servidores donde la cuenta no puede ejecutar <c>BACKUP DATABASE</c> (como Somee):
/// <list type="bullet">
/// <item><b>Exportar</b> solo lee. Todas las tablas se leen en una misma transacción (instantánea si la base la permite;
/// si no, serializable, que hace esperar a las escrituras durante la lectura), así que el respaldo es consistente. El
/// archivo es un ZIP con un manifiesto (migraciones, columnas, filas y SHA-256 de cada tabla) y una línea JSON por fila.</item>
/// <item><b>Verificar</b> comprueba los hashes y las filas del archivo sin conectarse a ninguna base.</item>
/// <item><b>Restaurar</b> carga el respaldo en una base <b>vacía</b>: si no tiene migraciones, aplica exactamente las del
/// respaldo; si ya tiene datos distintos, no hace nada. Carga todo en una transacción con las restricciones activas y,
/// antes de confirmarla, vuelve a leer cada tabla y exige el mismo hash del manifiesto.</item>
/// </list>
/// El archivo contiene datos personales, hashes de contraseñas y las claves de Data Protection: es tan sensible como la
/// base. Por eso se escribe con permisos solo para el dueño y nunca dentro de un repositorio Git.
/// </summary>
public sealed class DatabaseBackup(FixTrackDbContext context, TimeProvider clock)
{
    private const int RowsPerBatch = 2000;

    public sealed record ExportResult(string FilePath, string FileSha256, BackupManifest Manifest);

    public sealed record RestoreResult(BackupManifest Manifest, bool MigratedEmptyDatabase, IReadOnlyList<string> AlreadyPresent, IReadOnlyList<string> PendingMigrations);

    public async Task<ExportResult> ExportAsync(string directory, CancellationToken cancellationToken)
    {
        var folder = Path.GetFullPath(directory);
        EnsureOutsideRepository(folder);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        else
        {
            Directory.CreateDirectory(folder);
        }

        var connection = await OpenAsync(cancellationToken);
        var createdAt = clock.GetUtcNow().UtcDateTime;
        var name = $"fixtrack-respaldo-{createdAt:yyyyMMdd-HHmmss}Z";
        var finalPath = Path.Combine(folder, name + ".zip");
        for (var copy = 2; File.Exists(finalPath) || File.Exists(finalPath + ".parcial"); copy++)
        {
            finalPath = Path.Combine(folder, $"{name}-{copy}.zip");
        }

        var partialPath = finalPath + ".parcial";

        try
        {
            var isolation = await SupportsSnapshotAsync(connection, cancellationToken) ? IsolationLevel.Snapshot : IsolationLevel.Serializable;
            BackupManifest manifest;
            await using (var file = CreatePrivateFile(partialPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(isolation, cancellationToken))
            {
                var catalog = await BackupFormat.ReadCatalogAsync(connection, transaction, cancellationToken);
                var tables = new List<BackupTable>();
                foreach (var table in catalog)
                {
                    var entry = new BackupTable(table.Name, table.Columns, 0, string.Empty);
                    await using var stream = zip.CreateEntry(entry.EntryName, CompressionLevel.Optimal).Open();
                    var (rows, sha) = await BackupFormat.WriteRowsAsync(connection, transaction, table.Name, table.Columns, table.PrimaryKey, stream, cancellationToken);
                    tables.Add(entry with { Rows = rows, Sha256 = sha });
                }

                manifest = new BackupManifest(
                    BackupManifest.CurrentFormat,
                    createdAt,
                    connection.Database,
                    isolation == IsolationLevel.Snapshot ? "snapshot" : "serializable",
                    await BackupFormat.ReadMigrationsAsync(connection, transaction, cancellationToken),
                    tables);
                await transaction.CommitAsync(cancellationToken);

                await using var manifestStream = zip.CreateEntry(BackupManifest.EntryName, CompressionLevel.Optimal).Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, BackupFormat.ManifestJson, cancellationToken);
            }

            File.Move(partialPath, finalPath);
            return new ExportResult(finalPath, await FileSha256Async(finalPath, cancellationToken), manifest);
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }

    /// <summary>Comprueba el archivo sin conectarse a ninguna base: cada tabla debe tener sus filas y su hash.</summary>
    public static async Task<BackupManifest> VerifyAsync(string file, CancellationToken cancellationToken)
    {
        using var zip = OpenArchive(file);
        var manifest = await ReadManifestAsync(zip, cancellationToken);
        foreach (var table in manifest.Tables)
        {
            var entry = zip.GetEntry(table.EntryName) ?? throw new BackupException($"Falta el archivo de la tabla {table.Name}.");
            await using var stream = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long lines = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                lines += buffer.AsSpan(0, read).Count((byte)'\n');
            }

            if (lines != table.Rows || Convert.ToHexStringLower(hash.GetHashAndReset()) != table.Sha256)
            {
                throw new BackupException($"El respaldo está dañado: la tabla {table.Name} no coincide con su manifiesto.");
            }
        }

        return manifest;
    }

    public async Task<RestoreResult> RestoreAsync(string file, CancellationToken cancellationToken)
    {
        var manifest = await VerifyAsync(file, cancellationToken);
        var connection = await OpenAsync(cancellationToken);

        var applied = await BackupFormat.ReadMigrationsAsync(connection, null, cancellationToken);
        var migrated = false;
        if (applied.Count == 0)
        {
            if ((await BackupFormat.ReadCatalogAsync(connection, null, cancellationToken)).Count > 0)
            {
                throw new BackupException("La base de destino tiene tablas pero no historial de migraciones: restaura en una base vacía.");
            }

            var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var missing = manifest.Migrations.Where(migration => !known.Contains(migration)).ToList();
            if (manifest.Migrations.Count == 0 || missing.Count > 0)
            {
                throw new BackupException($"Esta versión de la aplicación no tiene las migraciones del respaldo ({string.Join(", ", missing)}): usa una versión igual o más nueva.");
            }

            // Exactamente el esquema del respaldo; las migraciones posteriores se aplican después, con sus datos ya cargados.
            await context.GetService<IMigrator>().MigrateAsync(manifest.Migrations[^1], cancellationToken);
            migrated = true;
        }
        else if (!applied.SequenceEqual(manifest.Migrations, StringComparer.Ordinal))
        {
            throw new BackupException(
                $"La base de destino tiene otras migraciones ({applied.Count}) que el respaldo ({manifest.Migrations.Count}). " +
                "Restaura en una base vacía: el comando le aplica las migraciones del respaldo.");
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var catalog = (await BackupFormat.ReadCatalogAsync(connection, transaction, cancellationToken)).ToDictionary(table => table.Name, StringComparer.Ordinal);
        if (!catalog.Keys.Order(StringComparer.Ordinal).SequenceEqual(manifest.Tables.Select(table => table.Name).Order(StringComparer.Ordinal)))
        {
            throw new BackupException("Las tablas de la base de destino no coinciden con las del respaldo.");
        }

        var alreadyPresent = new List<string>();
        foreach (var table in manifest.Tables)
        {
            var target = catalog[table.Name];
            if (!target.Columns.SequenceEqual(table.Columns))
            {
                throw new BackupException($"Las columnas de {table.Name} en la base de destino no coinciden con las del respaldo.");
            }

            var (rows, sha) = await BackupFormat.WriteRowsAsync(connection, transaction, table.Name, target.Columns, target.PrimaryKey, Stream.Null, cancellationToken);
            if (rows == 0)
            {
                continue;
            }

            // Solo se aceptan filas idénticas a las del respaldo: los datos iniciales que insertan las migraciones (roles).
            if (sha != table.Sha256)
            {
                throw new BackupException($"La tabla {table.Name} de la base de destino ya tiene datos: restaura en una base vacía. No se modificó nada.");
            }

            alreadyPresent.Add(table.Name);
        }

        using (var zip = OpenArchive(file))
        {
            foreach (var table in manifest.Tables.Where(table => table.Rows > 0 && !alreadyPresent.Contains(table.Name)))
            {
                await LoadAsync(connection, transaction, zip, table, cancellationToken);
            }
        }

        foreach (var table in manifest.Tables)
        {
            var (rows, sha) = await BackupFormat.WriteRowsAsync(connection, transaction, table.Name, table.Columns, catalog[table.Name].PrimaryKey, Stream.Null, cancellationToken);
            if (rows != table.Rows || sha != table.Sha256)
            {
                throw new BackupException($"Tras cargar {table.Name}, su contenido no coincide con el respaldo. No se confirmó nada.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var pending = context.Database.GetMigrations().Except(manifest.Migrations, StringComparer.Ordinal).ToList();
        return new RestoreResult(manifest, migrated, alreadyPresent, pending);
    }

    private static async Task LoadAsync(SqlConnection connection, SqlTransaction transaction, ZipArchive zip, BackupTable table, CancellationToken cancellationToken)
    {
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = "dbo." + BackupFormat.Bracket(table.Name),
            BulkCopyTimeout = 300,
        };
        using var batch = new DataTable();
        foreach (var column in table.Columns)
        {
            batch.Columns.Add(column.Name, BackupFormat.ClrType(column.Type));
            bulk.ColumnMappings.Add(column.Name, column.Name);
        }

        await using var stream = zip.GetEntry(table.EntryName)!.Open();
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            using var row = JsonDocument.Parse(line);
            var values = row.RootElement.EnumerateArray().ToList();
            if (values.Count != table.Columns.Count)
            {
                throw new BackupException($"Una fila de {table.Name} no tiene las columnas del manifiesto.");
            }

            batch.Rows.Add([.. values.Select((value, index) => BackupFormat.ReadValue(value, table.Columns[index].Type))]);
            if (batch.Rows.Count == RowsPerBatch)
            {
                await bulk.WriteToServerAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Rows.Count > 0)
        {
            await bulk.WriteToServerAsync(batch, cancellationToken);
        }
    }

    /// <summary>La conexión del contexto, abierta. EF la cierra al liberar el contexto.</summary>
    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        return connection;
    }

    private static async Task<bool> SupportsSnapshotAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = BackupFormat.Command(connection, null, "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static ZipArchive OpenArchive(string file)
    {
        if (!File.Exists(file))
        {
            throw new BackupException($"No existe el archivo {Path.GetFullPath(file)}.");
        }

        try
        {
            return ZipFile.OpenRead(file);
        }
        catch (InvalidDataException)
        {
            throw new BackupException("El archivo no es un respaldo de FixTrack (ZIP no válido).");
        }
    }

    private static async Task<BackupManifest> ReadManifestAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(BackupManifest.EntryName) ?? throw new BackupException("El archivo no tiene manifiesto: no es un respaldo de FixTrack.");
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, BackupFormat.ManifestJson, cancellationToken);
        if (manifest is not { Format: BackupManifest.CurrentFormat })
        {
            throw new BackupException("Formato de respaldo desconocido.");
        }

        return manifest;
    }

    /// <summary>Los respaldos tienen datos personales y hashes de contraseñas: nunca dentro de un repositorio Git.</summary>
    internal static void EnsureOutsideRepository(string folder)
    {
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                throw new BackupException(
                    $"{folder} está dentro del repositorio Git {current.FullName}. Los respaldos contienen datos personales y " +
                    "hashes de contraseñas y no deben llegar a GitHub: usa una carpeta fuera de cualquier repositorio.");
            }
        }
    }

    private static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    private static async Task<string> FileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
    }
}
