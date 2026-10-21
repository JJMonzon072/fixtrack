using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;
using FixTrack.Tests.Application;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Centro de reportes calculado con agregaciones en SQL (SQLite en memoria; SQL Server en SqlServer/).</summary>
public sealed class ReportRepositoryTests : IDisposable
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero));

    private readonly SqliteTestDatabase _database = new();
    private readonly CommandCounter _commands = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private FixTrackDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlite(_database.Connection).AddInterceptors(_commands).Options);

    private async Task<OperationsReport> ReportAsync(ReportFilter filter)
    {
        await using var context = CreateContext();
        return await new ReportService(new ReportRepository(context), Clock).GetAsync(filter, Token);
    }

    private async Task<ReportExport> ExportAsync(ReportFilter filter, ReportExportBasis basis, int maxRows = 100)
    {
        await using var context = CreateContext();
        return await new ReportService(new ReportRepository(context), Clock).ExportAsync(filter, basis, maxRows, Token);
    }

    [Fact]
    public async Task October_indicators_use_the_date_of_each_event()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);

        var report = await ReportAsync(new ReportFilter(ReportSeed.October));

        ReportExpectations.AssertOctoberTotals(report);
        ReportExpectations.AssertOctoberDistributions(report);
        ReportExpectations.AssertOctoberSeries(report);
        ReportExpectations.AssertOctoberTechnicians(report);
        Assert.True(report.HasActivity);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, report.CalculatedAtUtc);
    }

    [Theory]
    [InlineData(1, 1, 1, 0)] // A recibida 00:00 local del 1: incluida.
    [InlineData(31, 31, 1, 0)] // C recibida 23:59 local del 31: incluida.
    [InlineData(3, 4, 0, 2)] // B (3 de octubre) y A (4 de octubre) entregadas.
    public async Task Day_boundaries_are_inclusive_at_the_start_and_exclusive_at_the_end(int fromDay, int toDay, int received, int delivered)
    {
        await ReportSeed.SeedAsync(CreateContext, Token);
        var period = ReportPeriod.Create(new DateOnly(2026, 10, fromDay), new DateOnly(2026, 10, toDay), ReportSeed.Guatemala);

        var report = await ReportAsync(new ReportFilter(period));

        Assert.Equal((received, delivered), (report.Received, report.Delivered));
    }

    [Fact]
    public async Task The_first_minute_of_the_next_day_belongs_to_the_next_period()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);
        var november = ReportPeriod.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), ReportSeed.Guatemala);

        var report = await ReportAsync(new ReportFilter(november));

        // D (00:00 local del 1 de noviembre) y la cancelación de H (2 de noviembre).
        Assert.Equal((1, 0, 1), (report.Received, report.Delivered, report.Cancelled));
    }

    [Fact]
    public async Task An_empty_period_shows_zeros_and_no_invented_averages()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);
        var period = ReportPeriod.Create(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31), ReportSeed.Guatemala);

        var report = await ReportAsync(new ReportFilter(period));

        Assert.False(report.HasActivity);
        Assert.Equal((0, 0, 0, 0m), (report.Received, report.Delivered, report.Cancelled, report.Revenue));
        Assert.Null(report.AverageFinalCost);
        Assert.Null(report.AverageHoursToDelivery);
        Assert.Null(report.AverageHoursToReady);
        Assert.Null(report.OnTimePercent);
        Assert.Null(report.CostVariance);
        Assert.Null(report.CostVariancePercent);
        Assert.Equal(31, report.Series.Count);
        Assert.All(report.Series, point => Assert.Equal((0, 0, 0m), (point.Received, point.Delivered, point.Revenue)));
        Assert.All(report.ReceivedByStatus, share => Assert.Equal((0, 0), (share.Count, share.Percent)));

        // Las órdenes abiertas de ahora no dependen del período.
        Assert.Equal([("Carla Ruiz", 1), ("Sin técnico asignado", 2)], report.Technicians.Select(row => (row.Name, row.OpenNow)));
    }

    [Fact]
    public async Task Without_promised_dates_the_punctuality_has_no_data()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);

        // El 3 de octubre solo se entregó B (prioridad Normal, sin fecha comprometida ni costo estimado).
        var period = ReportPeriod.Create(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 3), ReportSeed.Guatemala);
        var report = await ReportAsync(new ReportFilter(period, Priority: ServiceOrderPriority.Normal));

        Assert.Equal((1, 0, 0), (report.Delivered, report.DeliveredWithPromise, report.DeliveredLate));
        Assert.Null(report.OnTimePercent);
        Assert.Null(report.CostVariance);
        Assert.Equal(200.00m, report.AverageFinalCost);
    }

    [Fact]
    public async Task Filters_narrow_the_orders_before_each_indicator()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);
        await using var context = CreateContext();
        var carla = await context.Technicians.Where(technician => technician.FullName == "Carla Ruiz").Select(technician => technician.Id).SingleAsync(Token);

        var byTechnician = await ReportAsync(new ReportFilter(ReportSeed.October, TechnicianId: carla));
        var delivered = await ReportAsync(new ReportFilter(ReportSeed.October, Status: ServiceOrderStatus.Delivered));
        var normal = await ReportAsync(new ReportFilter(ReportSeed.October, Priority: ServiceOrderPriority.Normal));
        var unassigned = await ReportAsync(new ReportFilter(ReportSeed.October, UnassignedOnly: true, TechnicianId: carla));
        var cancelled = await ReportAsync(new ReportFilter(ReportSeed.October, Status: ServiceOrderStatus.Cancelled));

        Assert.Equal((4, 2, 0, 700.00m), (byTechnician.Received, byTechnician.Delivered, byTechnician.Cancelled, byTechnician.Revenue));
        Assert.Equal("Carla Ruiz", Assert.Single(byTechnician.Technicians).Name);
        Assert.Equal((3, 4, 0), (delivered.Received, delivered.Delivered, delivered.Cancelled));
        Assert.Equal((2, 3, 1), (normal.Received, normal.Delivered, normal.Cancelled));
        Assert.Equal((1, 0, 0), (unassigned.Received, unassigned.Delivered, unassigned.Cancelled));
        Assert.Equal((2, 0, 2), (cancelled.Received, cancelled.Delivered, cancelled.Cancelled));
    }

    [Fact]
    public async Task The_number_of_queries_does_not_grow_with_the_data()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);
        _commands.Reset();
        var single = await ReportAsync(new ReportFilter(ReportSeed.October));
        var queriesForOneCopy = _commands.Count;

        await ReportSeed.SeedAsync(CreateContext, Token, copies: 3, firstCopy: 2);
        _commands.Reset();
        var tripled = await ReportAsync(new ReportFilter(ReportSeed.October));

        Assert.Equal(8, queriesForOneCopy);
        Assert.Equal(queriesForOneCopy, _commands.Count);
        Assert.Equal((single.Received * 4, single.Revenue * 4), (tripled.Received, tripled.Revenue));
        Assert.Equal(single.AverageHoursToDelivery, tripled.AverageHoursToDelivery);
        Assert.Equal(9, tripled.Technicians.Count);
    }

    [Fact]
    public async Task Exports_follow_the_chosen_date_in_order_without_contact_data()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);

        var received = await ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Received);
        var delivered = await ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Delivered);

        Assert.False(received.TooLarge);
        Assert.Equal(
            ["No enciende", "Escáner", "Teclado", "No imprime", "Bisagra", "Batería", "Pantalla con líneas"],
            received.Rows.Select(row => row.ReportedIssue));
        Assert.Equal(["Atasco de papel", "No enciende", "Escáner", "Teclado"], delivered.Rows.Select(row => row.ReportedIssue));

        var a = delivered.Rows[1];
        Assert.Equal(("Ana Morales", "Dell", "Latitude 5440", "RPT-DELL-1", "Carla Ruiz", 400.00m, 450.00m),
            (a.CustomerName, a.DeviceBrand, a.DeviceModel, a.SerialNumber, a.TechnicianName, a.EstimatedCost, a.FinalCost));
        Assert.DoesNotContain(typeof(ReportExportRow).GetProperties(), property =>
            property.Name.Contains("Phone", StringComparison.Ordinal) || property.Name.Contains("Email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_export_larger_than_the_limit_returns_no_rows_and_says_so()
    {
        await ReportSeed.SeedAsync(CreateContext, Token);

        var tooLarge = await ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Received, maxRows: 6);
        var exact = await ExportAsync(new ReportFilter(ReportSeed.October), ReportExportBasis.Received, maxRows: 7);

        Assert.Equal((true, 0, 6), (tooLarge.TooLarge, tooLarge.Rows.Count, tooLarge.MaxRows));
        Assert.Equal((false, 7), (exact.TooLarge, exact.Rows.Count));
    }

    [Fact]
    public async Task Daily_series_follow_local_days_across_a_daylight_saving_change()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        await ReportSeed.SeedAsync(CreateContext, Token);
        var period = ReportPeriod.Create(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), newYork);
        await using (var context = CreateContext())
        {
            var device = await context.Devices.Include(item => item.Customer).FirstAsync(Token);
            // 04:30 UTC del 8 de marzo = 23:30 del 7 (EST); 07:30 UTC = 03:30 del 8 (EDT, ya en horario de verano).
            context.ServiceOrders.Add(new ServiceOrder(device, "Antes del cambio", ServiceOrderPriority.Normal, new DateTime(2026, 3, 8, 4, 30, 0, DateTimeKind.Utc), null, null, null));
            context.ServiceOrders.Add(new ServiceOrder(device, "Después del cambio", ServiceOrderPriority.Normal, new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), null, null, null));
            await context.SaveChangesAsync(Token);
        }

        _commands.Reset();
        var report = await ReportAsync(new ReportFilter(period));

        Assert.Equal((1, 1), (report.Series[6].Received, report.Series[7].Received));
        Assert.Equal(2, report.Received);
        Assert.Equal(10, _commands.Count); // dos tramos de desfase: las dos series diarias consultan dos veces.
    }
}
