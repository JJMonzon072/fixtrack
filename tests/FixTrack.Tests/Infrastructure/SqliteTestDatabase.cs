using System.Globalization;
using FixTrack.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.Infrastructure;

/// <summary>
/// Base de datos SQLite en memoria para pruebas aisladas: no requiere SQL Server ni credenciales
/// y desaparece al cerrar la conexión. Nunca se conecta a la base remota.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private static readonly CompareInfo Spanish = CultureInfo.GetCultureInfo("es").CompareInfo;

    public SqliteTestDatabase()
    {
        Connection = CreateOpenConnection();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SqliteConnection Connection { get; }

    public FixTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlite(Connection).Options);

    /// <summary>Conexión abierta con la intercalación que usa el modelo de SQL Server.</summary>
    public static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.CreateCollation(
            "Modern_Spanish_100_CI_AI",
            (x, y) => Spanish.Compare(x, y, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));
        return connection;
    }

    public void Dispose() => Connection.Dispose();
}
