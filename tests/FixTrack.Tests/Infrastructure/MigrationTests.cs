using System.Text.RegularExpressions;
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
            "El modelo cambió sin una migración. Ejecuta: dotnet ef migrations add <Nombre> --context FixTrackDbContext --project src/FixTrack.Infrastructure --startup-project src/FixTrack.Web");
    }

    [Fact]
    public void The_migrations_create_the_schema_without_destructive_operations()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        foreach (var table in new[] { "Customers", "Devices", "Technicians", "ServiceOrders", "ServiceOrderStatusHistory", "ServiceOrderAssignments" })
        {
            Assert.Contains($"CREATE TABLE [{table}]", script);
        }

        Assert.Contains("TR_ServiceOrderStatusHistory_AppendOnly", script);
        Assert.Contains("TR_ServiceOrderAssignments_AppendOnly", script);
        Assert.Contains("INSERT INTO [ServiceOrderAssignments]", script);

        foreach (var forbidden in new[] { "DROP TABLE", "DROP COLUMN", "DROP INDEX", "TRUNCATE", "DELETE FROM", "UPDATE [" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }

        // Ninguna tabla del negocio borra en cascada. Solo las tablas internas de Identity (AspNet*) lo hacen entre sí:
        // por ejemplo, los roles de un usuario se eliminan con el usuario.
        var tables = Regex.Matches(script, @"CREATE TABLE \[(?<table>\w+)\] \((?<body>.*?)\n\s*\);", RegexOptions.Singleline);
        var cascadesInIdentityTables = tables
            .Where(match => match.Groups["table"].Value.StartsWith("AspNet", StringComparison.Ordinal))
            .Sum(match => Regex.Count(match.Groups["body"].Value, "ON DELETE CASCADE"));
        Assert.Equal(Regex.Count(script, "ON DELETE CASCADE"), cascadesInIdentityTables);

        // ALTER COLUMN incluye un bloque estándar de EF que elimina la restricción DEFAULT de la columna, si existe.
        // Es el único DROP permitido.
        Assert.Equal(
            Regex.Count(script, @"\[sys\]\.\[default_constraints\]"),
            Regex.Count(script, "DROP CONSTRAINT", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void The_phase_2b_migration_requires_a_specialty_and_non_empty_serial_numbers()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020031151_InitialCreate",
            toMigration: "20261020045452_TechnicianSpecialtyAndDeviceSerialRules");

        Assert.Contains("ALTER COLUMN [Specialty] nvarchar(100) NOT NULL", script);
        Assert.Contains("CK_Technicians_Specialty_NotEmpty", script);
        Assert.Contains("CK_Devices_SerialNumber_NotEmpty", script);

        // Sin valor por defecto: no rellena datos existentes en silencio.
        Assert.DoesNotContain("SET [Specialty]", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_phase_3_migration_only_adds_order_columns_and_rules()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020045452_TechnicianSpecialtyAndDeviceSerialRules",
            toMigration: "20261020060713_ServiceOrderWorkflowFields");

        Assert.Contains("ADD [IntakeNotes] nvarchar(500) NULL", script);
        Assert.Contains("ADD [TechnicianAssignedAtUtc] datetime2(3) NULL", script);
        Assert.Contains("ADD [Version] int NOT NULL DEFAULT 0", script);
        Assert.Contains("CREATE INDEX [IX_ServiceOrders_ReceivedAtUtc]", script);
        Assert.Contains("CK_ServiceOrders_FinalCost_OnDelivery", script);
        Assert.Contains("CK_ServiceOrders_TechnicianAssignment", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER COLUMN", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_phase_4_assignments_migration_only_adds_authors_and_an_append_only_table()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020071400_IdentityAndDataProtectionKeys",
            toMigration: "20261020162649_ServiceOrderAuthorsAndAssignments");

        // Columnas nuevas que admiten NULL: los registros de historial existentes no se modifican.
        Assert.Contains("ALTER TABLE [ServiceOrderStatusHistory] ADD [ChangedByName] nvarchar(150) NULL", script);
        Assert.Contains("ALTER TABLE [ServiceOrderStatusHistory] ADD [ChangedByUserId] nvarchar(450) NULL", script);
        Assert.Contains("CREATE TABLE [ServiceOrderAssignments]", script);
        Assert.Contains("CK_ServiceOrderAssignments_DifferentTechnician", script);
        Assert.Contains("TR_ServiceOrderAssignments_AppendOnly", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER COLUMN", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_phase_4_audit_migration_only_adds_an_append_only_table()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020162649_ServiceOrderAuthorsAndAssignments",
            toMigration: "20261020164604_AuditEvents");

        Assert.Contains("CREATE TABLE [AuditEvents]", script);
        Assert.Contains("TR_AuditEvents_AppendOnly", script);
        Assert.Contains("CREATE INDEX [IX_AuditEvents_OccurredAtUtc]", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE [", script.Replace("ALTER TABLE [AuditEvents]", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void The_phase_4_versions_migration_only_adds_columns_with_a_default()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020164604_AuditEvents",
            toMigration: MigrationId("RecordVersions"));

        foreach (var table in new[] { "Customers", "Devices", "Technicians" })
        {
            Assert.Contains($"ALTER TABLE [{table}] ADD [Version] int NOT NULL DEFAULT 0", script);
        }

        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_phase_4_creation_request_migration_only_adds_a_nullable_column_and_its_index()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: MigrationId("RecordVersions"),
            toMigration: MigrationId("OrderCreationRequests"));

        Assert.Contains("ALTER TABLE [ServiceOrders] ADD [CreationRequestId] uniqueidentifier NULL", script);
        Assert.Contains("CREATE UNIQUE INDEX [UX_ServiceOrders_CreationRequestId]", script);
        Assert.Contains("WHERE [CreationRequestId] IS NOT NULL", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_phase_4_backfill_only_inserts_the_current_assignment_of_orders_without_records()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: MigrationId("OrderCreationRequests"),
            toMigration: MigrationId("BackfillCurrentAssignments"));

        Assert.Contains("INSERT INTO [ServiceOrderAssignments]", script);
        Assert.Contains("o.[TechnicianAssignedAtUtc]", script);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM [ServiceOrderAssignments]", script);
        Assert.Contains("ORDER BY o.[Id]", script);
        foreach (var forbidden in new[] { "DROP", "ALTER", "UPDATE", "DELETE", "TRUNCATE" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_read_only_role_migration_only_inserts_the_consulta_role()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: MigrationId("BackfillCurrentAssignments"),
            toMigration: MigrationId("ReadOnlyRole"));

        Assert.Contains("INSERT INTO [AspNetRoles]", script);
        Assert.Contains("N'Consulta', N'CONSULTA'", script);
        foreach (var forbidden in new[] { "DROP", "ALTER", "UPDATE", "DELETE", "TRUNCATE", "AspNetUsers", "AspNetUserRoles" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string MigrationId(string name)
    {
        using var context = CreateSqlServerContext();
        return context.Database.GetMigrations().Single(id => id.EndsWith("_" + name, StringComparison.Ordinal));
    }

    [Fact]
    public void The_identity_migration_creates_the_account_tables_and_both_roles()
    {
        using var context = CreateSqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: "20261020060713_ServiceOrderWorkflowFields",
            toMigration: "20261020071400_IdentityAndDataProtectionKeys");

        foreach (var table in new[] { "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims", "DataProtectionKeys" })
        {
            Assert.Contains($"CREATE TABLE [{table}]", script);
        }

        Assert.Contains("N'Administrador', N'ADMINISTRADOR'", script);
        Assert.Contains("N'Operador', N'OPERADOR'", script);
        Assert.Contains("CREATE UNIQUE INDEX [UserNameIndex]", script);
        Assert.Contains("CK_AspNetUsers_FullName_NotEmpty", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE [ServiceOrders]", script, StringComparison.Ordinal);
    }
}
