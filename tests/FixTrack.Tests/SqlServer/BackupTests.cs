using FixTrack.Domain.Auditing;
using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Backups;
using FixTrack.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// Respaldo lógico y restauración en SQL Server real. Cada prueba usa sus propias bases desechables (vacías al crearse)
/// y escribe los respaldos en una carpeta temporal fuera del repositorio.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BackupTests(DisposableSqlServerDatabase database) : IDisposable
{
    /// <summary>La última migración de la fase 3: el esquema que tiene hoy la base de Somee.</summary>
    private const string Phase3Migration = "20261020071400_IdentityAndDataProtectionKeys";

    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>Comillas, emoji, tabulador, barra invertida y salto de línea: todo lo que JSON debe escapar.</summary>
    private const string Notes = "Dijo \"urgente\" 😀\nLínea 2\tcon \\barra";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fixtrack-respaldos-pruebas-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static FixTrackDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connectionString).Options);

    private async Task<DatabaseBackup.ExportResult> ExportAsync(string connectionString)
    {
        await using var context = Context(connectionString);
        return await new DatabaseBackup(context, TimeProvider.System).ExportAsync(_folder, Token);
    }

    private static async Task<DatabaseBackup.RestoreResult> RestoreAsync(string connectionString, string file)
    {
        await using var context = Context(connectionString);
        return await new DatabaseBackup(context, TimeProvider.System).RestoreAsync(file, Token);
    }

    private async Task<string> EmptyDatabaseAsync()
    {
        Assert.SkipUnless(DisposableSqlServerDatabase.IsConfigured, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");
        return await database.CreateEmptyDatabaseAsync();
    }

    /// <summary>Base con el esquema actual, órdenes con historial y valores difíciles: texto con escapes, decimales, fechas con zona.</summary>
    private async Task<string> SourceDatabaseAsync()
    {
        var connectionString = await EmptyDatabaseAsync();
        await using (var context = Context(connectionString))
        {
            await context.Database.MigrateAsync(Token);
        }

        await ServiceOrderSeed.SeedAsync(() => Context(connectionString), Now, Token);
        await using (var context = Context(connectionString))
        {
            context.Customers.Add(new Customer("Zoë Ñandú Pérez", "5555-0101", "zoe@fixtrack.test", Notes, Now));
            context.Users.Add(new FixTrackUser
            {
                UserName = "respaldo@fixtrack.test",
                NormalizedUserName = "RESPALDO@FIXTRACK.TEST",
                Email = "respaldo@fixtrack.test",
                NormalizedEmail = "RESPALDO@FIXTRACK.TEST",
                FullName = "Cuenta De Respaldo",
                CreatedAtUtc = Now,
                LockoutEnabled = true,
                LockoutEnd = new DateTimeOffset(2026, 10, 21, 8, 30, 15, 123, TimeSpan.FromHours(-6)),
                SecurityStamp = Guid.NewGuid().ToString("N"),
            });
            context.AuditEvents.Add(new AuditEvent("prueba.respaldo", "Customer", "1", "Evento de prueba", Actor.System, Now));
            context.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "key-prueba", Xml = "<key id=\"prueba\" />" });
            await context.SaveChangesAsync(Token);
        }

        return connectionString;
    }

    [Fact]
    public async Task A_backup_restored_into_an_empty_database_is_identical_and_keeps_working()
    {
        var source = await SourceDatabaseAsync();
        var target = await EmptyDatabaseAsync();

        var backup = await ExportAsync(source);
        var restore = await RestoreAsync(target, backup.FilePath);
        var copy = await ExportAsync(target);
        int maxSourceCustomer;
        await using (var context = Context(source))
        {
            maxSourceCustomer = await context.Customers.MaxAsync(customer => customer.Id, Token);
        }

        await using var restored = Context(target);
        var added = new Customer("Cliente Posterior", "5555-0199", null, null, Now);
        restored.Customers.Add(added);
        await restored.SaveChangesAsync(Token);

        var manifest = backup.Manifest;
        Assert.StartsWith("fixtrack-respaldo-", Path.GetFileName(backup.FilePath));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(backup.FilePath));
        }

        Assert.Equal("serializable", manifest.Isolation);
        Assert.Equal(Context(source).Database.GetMigrations(), manifest.Migrations);
        Assert.Contains(manifest.Tables, table => table.Name == "AspNetUsers" && table.Rows == 1);
        Assert.Contains(manifest.Tables, table => table.Name == "ServiceOrderStatusHistory" && table.Rows > 0);
        Assert.DoesNotContain(manifest.Tables, table => table.Name == "__EFMigrationsHistory");
        Assert.True(manifest.Tables.ToList().FindIndex(table => table.Name == "Customers") < manifest.Tables.ToList().FindIndex(table => table.Name == "Devices"));
        Assert.True(restore.MigratedEmptyDatabase);
        Assert.Equal(["AspNetRoles"], restore.AlreadyPresent);
        Assert.Empty(restore.PendingMigrations);
        Assert.Equal(
            manifest.Tables.Select(table => (table.Name, table.Rows, table.Sha256)),
            copy.Manifest.Tables.Select(table => (table.Name, table.Rows, table.Sha256)));
        Assert.True(added.Id > maxSourceCustomer);
        Assert.Equal(Notes, (await restored.Customers.SingleAsync(c => c.Phone == "5555-0101", Token)).Notes);
    }

    [Fact]
    public async Task Restoring_over_existing_data_is_refused_and_changes_nothing()
    {
        var source = await SourceDatabaseAsync();
        var target = await SourceDatabaseAsync();
        await using (var context = Context(target))
        {
            context.Customers.Add(new Customer("Solo En Destino", "5555-0177", null, null, Now));
            await context.SaveChangesAsync(Token);
        }

        var backup = await ExportAsync(source);
        var before = await ExportAsync(target);

        var error = await Assert.ThrowsAsync<BackupException>(() => RestoreAsync(target, backup.FilePath));
        var after = await ExportAsync(target);

        Assert.Contains("ya tiene datos", error.Message);
        Assert.Equal(
            before.Manifest.Tables.Select(table => table.Sha256),
            after.Manifest.Tables.Select(table => table.Sha256));
    }

    [Fact]
    public async Task A_phase_3_backup_is_restored_with_its_own_schema_and_then_upgraded()
    {
        var source = await EmptyDatabaseAsync();
        await using (var context = Context(source))
        {
            await context.GetService<IMigrator>().MigrateAsync(Phase3Migration, Token);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Customers (FullName, Phone, IsActive, CreatedAtUtc) VALUES (N'Cliente Fase Tres', '5555-0303', 1, '2026-10-19T10:00:00');
                INSERT INTO Technicians (FullName, Specialty, IsActive, CreatedAtUtc) VALUES (N'Técnica Fase Tres', N'Laptops', 1, '2026-10-19T10:00:00');
                INSERT INTO Devices (CustomerId, Type, Brand, CreatedAtUtc) VALUES (1, 1, N'Dell', '2026-10-19T10:00:00');
                INSERT INTO ServiceOrders (DeviceId, Status, Priority, ReportedIssue, ReceivedAtUtc, CreatedAtUtc, TechnicianId, TechnicianAssignedAtUtc)
                VALUES (1, 1, 2, N'No enciende', '2026-10-19T10:05:00', '2026-10-19T10:05:00', 1, '2026-10-19T11:00:00'),
                       (1, 1, 2, N'Sin técnico', '2026-10-19T10:06:00', '2026-10-19T10:06:00', NULL, NULL);
                """,
                Token);
        }

        var target = await EmptyDatabaseAsync();
        var backup = await ExportAsync(source);
        var restore = await RestoreAsync(target, backup.FilePath);
        await using var restored = Context(target);
        await restored.Database.MigrateAsync(Token);

        Assert.Equal(Phase3Migration, backup.Manifest.Migrations[^1]);
        Assert.DoesNotContain(backup.Manifest.Tables, table => table.Name == "AuditEvents");
        Assert.True(restore.MigratedEmptyDatabase);
        // Todas las migraciones posteriores a la fase 3 (sin fijar cuántas: las fases siguientes agregan más).
        Assert.Equal(restored.Database.GetMigrations().SkipWhile(id => id != Phase3Migration).Skip(1), restore.PendingMigrations);
        var customer = await restored.Customers.SingleAsync(Token);
        Assert.Equal(("Cliente Fase Tres", 0), (customer.FullName, customer.Version));

        // La asignación vigente de la orden con técnico queda registrada con su fecha real y sin autor.
        var assignment = await restored.ServiceOrderAssignments.SingleAsync(Token);
        Assert.Equal((1, (int?)null, 1, new DateTime(2026, 10, 19, 11, 0, 0), (string?)null),
            (assignment.ServiceOrderId, assignment.FromTechnicianId, assignment.ToTechnicianId, assignment.AssignedAtUtc, assignment.AssignedByName));
    }

    [Fact]
    public async Task A_database_with_other_migrations_is_not_a_restore_target()
    {
        var source = await SourceDatabaseAsync();
        var target = await EmptyDatabaseAsync();
        await using (var context = Context(target))
        {
            await context.GetService<IMigrator>().MigrateAsync(Phase3Migration, Token);
        }

        var backup = await ExportAsync(source);

        var error = await Assert.ThrowsAsync<BackupException>(() => RestoreAsync(target, backup.FilePath));

        Assert.Contains("otras migraciones", error.Message);
    }
}
