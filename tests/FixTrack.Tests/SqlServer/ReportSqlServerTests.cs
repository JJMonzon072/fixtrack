using FixTrack.Application.Reports;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;
using FixTrack.Tests.Application;
using FixTrack.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// Las consultas del centro de reportes contra SQL Server real: deben traducirse por completo (sin evaluación en el
/// cliente) y dar exactamente los mismos resultados que con SQLite. Usan una base desechable propia y vacía.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReportSqlServerTests(DisposableSqlServerDatabase database)
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<string> SeededDatabaseAsync()
    {
        Assert.SkipUnless(DisposableSqlServerDatabase.IsConfigured, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");
        var connectionString = await database.CreateEmptyDatabaseAsync();
        await using (var context = Context(connectionString))
        {
            await context.Database.MigrateAsync(Token);
        }

        await ReportSeed.SeedAsync(() => Context(connectionString), Token);
        return connectionString;
    }

    private static FixTrackDbContext Context(string connectionString, CommandCounter? counter = null)
    {
        var options = new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connectionString);
        if (counter is not null)
        {
            options.AddInterceptors(counter);
        }

        return new FixTrackDbContext(options.Options);
    }

    [Fact]
    public async Task SQL_Server_calculates_the_same_october_report_in_eight_queries()
    {
        var connectionString = await SeededDatabaseAsync();
        var counter = new CommandCounter();
        await using var context = Context(connectionString, counter);

        var report = await new ReportService(new ReportRepository(context), Clock).GetAsync(new ReportFilter(ReportSeed.October), Token);

        ReportExpectations.AssertOctoberTotals(report);
        ReportExpectations.AssertOctoberDistributions(report);
        ReportExpectations.AssertOctoberSeries(report);
        ReportExpectations.AssertOctoberTechnicians(report);
        Assert.Equal(8, counter.Count);
        Assert.Contains(counter.Commands, sql => sql.Contains("DATEDIFF_BIG(SECOND, '1970-01-01T00:00:00'", StringComparison.Ordinal));
        Assert.Contains(counter.Commands, sql => sql.Contains("DATEADD(minute", StringComparison.OrdinalIgnoreCase));
        Assert.All(counter.Commands, sql => Assert.DoesNotContain("SELECT [s].[Id], [s].[CreatedAtUtc]", sql, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SQL_Server_applies_the_same_filters_boundaries_and_export_order()
    {
        var connectionString = await SeededDatabaseAsync();
        await using var context = Context(connectionString);
        var service = new ReportService(new ReportRepository(context), Clock);
        var carla = await context.Technicians.Where(technician => technician.FullName == "Carla Ruiz").Select(technician => technician.Id).SingleAsync(Token);
        var firstDay = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), ReportSeed.Guatemala);
        var lastDay = ReportPeriod.Create(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 31), ReportSeed.Guatemala);

        var byTechnician = await service.GetAsync(new ReportFilter(ReportSeed.October, TechnicianId: carla), Token);
        var unassigned = await service.GetAsync(new ReportFilter(ReportSeed.October, UnassignedOnly: true), Token);
        var first = await service.GetAsync(new ReportFilter(firstDay), Token);
        var last = await service.GetAsync(new ReportFilter(lastDay), Token);
        var export = await service.ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Delivered, 10, Token);
        var tooLarge = await service.ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Received, 6, Token);

        Assert.Equal((4, 2, 700.00m), (byTechnician.Received, byTechnician.Delivered, byTechnician.Revenue));
        Assert.Equal((1, 0), (unassigned.Received, unassigned.Delivered));
        Assert.Equal((1, 1), (first.Received, last.Received));
        Assert.Equal(["Atasco de papel", "No enciende", "Escáner", "Teclado"], export.Rows.Select(row => row.ReportedIssue));
        Assert.Equal((450.00m, "Pantalla con líneas"), (export.Rows[1].FinalCost, (await service.ExportAsync(new ReportFilter(lastDay), ReportExportBasis.Received, 10, Token)).Rows.Single().ReportedIssue));
        Assert.True(tooLarge.TooLarge);
    }
}
