using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FixTrack.Tests.Infrastructure;

/// <summary>
/// Revisa las migraciones de SQL Server sin conectarse a ninguna base: la cadena de conexión es ficticia
/// y solo se usa para generar el modelo y el script.
/// </summary>
public sealed class MigrationTests
{
    private static FixTrackDbContext CreateSqlServerContext() =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>()
            .UseSqlServer("Server=sin-conexion;Database=FixTrackDB;Encrypt=True;")
            .Options);

    [Fact]
    public void Migrations_are_in_sync_with_the_model()
    {
        using var context = CreateSqlServerContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "El modelo cambió sin una migración. Ejecuta: dotnet ef migrations add <Nombre> --project src/FixTrack.Infrastructure --startup-project src/FixTrack.Web");
    }

    [Fact]
    public void The_initial_migration_creates_the_schema_without_destructive_operations()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        foreach (var table in new[] { "Customers", "Devices", "Technicians", "ServiceOrders", "ServiceOrderStatusHistory" })
        {
            Assert.Contains($"CREATE TABLE [{table}]", script);
        }

        Assert.Contains("TR_ServiceOrderStatusHistory_AppendOnly", script);
        Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.OrdinalIgnoreCase);
    }
}
