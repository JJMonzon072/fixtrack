using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// Base de datos SQL Server desechable para las pruebas que necesitan el motor real y, a veces, confirmar transacciones
/// (por ejemplo, operaciones concurrentes de dos administradores). Se activa con la variable
/// <c>FIXTRACK_TEST_SQLSERVER_SERVER</c>: una cadena de conexión a un servidor <b>local</b> (un contenedor de desarrollo
/// o el de GitHub Actions) donde la cuenta puede crear y eliminar bases. Crea una base con nombre único, aplica las
/// migraciones, carga datos ficticios y la elimina al terminar. Nunca se conecta a un servidor remoto como Somee.
/// </summary>
public sealed class DisposableSqlServerDatabase : IAsyncLifetime
{
    public const string ServerVariable = "FIXTRACK_TEST_SQLSERVER_SERVER";
    private const string NamePrefix = "FixTrackPrueba_";

    private string? _serverConnectionString;
    private string? _databaseName;

    /// <summary>Cadena de conexión a la base creada, o <c>null</c> si la variable no está definida.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Momento de referencia de los datos cargados (UTC).</summary>
    public DateTime SeededAtUtc { get; private set; }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServerVariable));

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(server);
        EnsureLocalServer(builder.DataSource);
        builder.InitialCatalog = "master";
        _serverConnectionString = builder.ConnectionString;
        _databaseName = NamePrefix + Guid.NewGuid().ToString("N");

        await using (var master = new SqlConnection(_serverConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        builder.InitialCatalog = _databaseName;
        ConnectionString = builder.ConnectionString;
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        SeededAtUtc = DateTime.UtcNow;
        await ServiceOrderSeed.SeedAsync(CreateContext, SeededAtUtc, CancellationToken.None);
        await SeedAccentedCustomerAsync(SeededAtUtc);
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverConnectionString is null || _databaseName is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(_serverConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
        await drop.ExecuteNonQueryAsync();
    }

    public FixTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>Solo se aceptan servidores en esta máquina: las pruebas crean y eliminan bases.</summary>
    internal static void EnsureLocalServer(string dataSource)
    {
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        host = host.Split(',')[0].Split('\\')[0].Trim();
        string[] local = ["localhost", "127.0.0.1", "::1", "[::1]", "(local)", "."];
        if (!local.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ServerVariable} debe apuntar a un servidor local desechable (localhost); se recibió «{host}». " +
                "Las pruebas crean y eliminan bases, así que nunca se ejecutan contra un servidor remoto.");
        }
    }

    /// <summary>Un cliente con tildes y una orden, para comprobar la intercalación española.</summary>
    private async Task SeedAccentedCustomerAsync(DateTime now)
    {
        await using var context = CreateContext();
        var jose = new Customer("José Ramírez", "5555-0003", null, null, now.AddDays(-30));
        context.Customers.Add(jose);
        await context.SaveChangesAsync();
        var tablet = jose.RegisterDevice(DeviceType.Tablet, "Samsung", "Galaxy Tab S9", null, null, now.AddDays(-30));
        await context.SaveChangesAsync();
        context.ServiceOrders.Add(new ServiceOrder(tablet, "Batería", ServiceOrderPriority.Normal, now.AddDays(-1), null, null, null));
        await context.SaveChangesAsync();
    }
}

/// <summary>Las clases de pruebas de SQL Server comparten una sola base desechable y se ejecutan en serie.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<DisposableSqlServerDatabase>
{
    public const string Name = "SQL Server";
}
