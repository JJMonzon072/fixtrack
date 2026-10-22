using FixTrack.Demo;
using FixTrack.Demo.Scenario;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// La demostración contra SQL Server real: la preparación del servidor (base y login propio) y la carga completa con
/// transacciones, bloqueos de aplicación y triggers del motor de producción.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DemoSqlServerTests(DisposableSqlServerDatabase database)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_scenario_loads_with_the_production_database_engine()
    {
        Assert.SkipUnless(DisposableSqlServerDatabase.IsConfigured, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");
        var connectionString = await database.CreateEmptyDatabaseAsync();
        await using (var context = new FixTrackDbContext(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connectionString).Options))
        {
            await context.Database.MigrateAsync(Token);
        }

        await using var services = DemoServices.Create(connectionString);
        var scenario = DemoScenario.Create(DemoScenarioTests.Anchor, DemoServices.WorkshopTimeZone);

        Assert.Equal(DemoLoadOutcome.Loaded, await new DemoLoader(services).LoadAsync(scenario, DemoLoaderTests.Passwords, Token));
        Assert.Equal(DemoLoadOutcome.AlreadyLoaded, await new DemoLoader(services).LoadAsync(scenario, DemoLoaderTests.Passwords, Token));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
        Assert.Equal(25, await db.ServiceOrders.CountAsync(Token));
        Assert.Equal(13, await db.ServiceOrders.CountAsync(order => order.Status == ServiceOrderStatus.Delivered, Token));
        Assert.Equal(104, await db.Set<ServiceOrderStatusChange>().CountAsync(Token));
        Assert.Equal(3, await db.Users.CountAsync(Token));
    }

    [Fact]
    public async Task Preparing_the_server_creates_a_login_that_only_owns_the_demo_database()
    {
        Assert.SkipUnless(DisposableSqlServerDatabase.IsConfigured, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");
        var server = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(DisposableSqlServerDatabase.ServerVariable));
        Assert.SkipUnless(server.UserID == "sa", "La preparación usa la cuenta sa del servidor desechable.");
        var host = server.DataSource.Split(',');
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var settings = new DemoSettings(
            host[0], host.Length > 1 ? int.Parse(host[1], System.Globalization.CultureInfo.InvariantCulture) : 1433,
            $"FixTrackDemoPrueba_{suffix}", $"fixtrack_demo_{suffix}", server.Password, $"App{Guid.NewGuid():N}"[..24] + "Aa1",
            "sin-uso.pfx", null, null, null, null);
        try
        {
            await DemoDatabase.PrepareAsync(settings, Token);
            await DemoDatabase.PrepareAsync(settings, Token);

            await using var app = new SqlConnection(settings.AppConnectionString());
            await app.OpenAsync(Token);
            await using var command = app.CreateCommand();
            command.CommandText = "SELECT DB_NAME(), IS_ROLEMEMBER('db_owner'), IS_SRVROLEMEMBER('sysadmin')";
            await using var reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            Assert.Equal(settings.Database, reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(settings.ServerAdminConnectionString());
            await master.OpenAsync(Token);
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"IF DB_ID('{settings.Database}') IS NOT NULL BEGIN ALTER DATABASE [{settings.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{settings.Database}]; END;" +
                $"IF SUSER_ID('{settings.AppLogin}') IS NOT NULL DROP LOGIN [{settings.AppLogin}];";
            await drop.ExecuteNonQueryAsync(Token);
        }
    }
}
