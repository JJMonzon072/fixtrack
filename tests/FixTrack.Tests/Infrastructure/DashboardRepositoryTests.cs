using FixTrack.Application.Dashboard;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Indicadores del panel calculados con agregaciones en SQL (SQLite en memoria).</summary>
public sealed class DashboardRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Counts_every_indicator_in_one_aggregation()
    {
        await ServiceOrderSeed.SeedAsync(_database, Now, Token);
        await using var context = _database.CreateContext();

        var counts = await new DashboardRepository(context).CountOrdersAsync(Now, Now.AddDays(-7), Token);

        Assert.Equal(new OrderCounts(
            Received: 1, Diagnosing: 1, InRepair: 1, AwaitingParts: 0, ReadyForPickup: 0, Delivered: 1, Cancelled: 1,
            Overdue: 1, Unassigned: 1, ReceivedRecently: 4, OpenLow: 0, OpenNormal: 1, OpenHigh: 1, OpenUrgent: 1), counts);
        Assert.Equal((3, 5, 2), (counts.Open, counts.Total, counts.HighPriorityOpen));
    }

    [Fact]
    public async Task An_empty_database_has_zero_in_every_indicator()
    {
        await using var context = _database.CreateContext();
        var repository = new DashboardRepository(context);

        Assert.Equal(OrderCounts.Empty, await repository.CountOrdersAsync(Now, Now.AddDays(-7), Token));
        Assert.Empty(await repository.ListUpcomingDeliveriesAsync(Now, 5, Token));
        Assert.Empty(await repository.ListRecentlyReceivedAsync(5, Token));
    }

    [Fact]
    public async Task Lists_upcoming_deliveries_and_recently_received_orders()
    {
        await ServiceOrderSeed.SeedAsync(_database, Now, Token);
        await using var context = _database.CreateContext();
        var repository = new DashboardRepository(context);

        var upcoming = await repository.ListUpcomingDeliveriesAsync(Now, 5, Token);
        var recent = await repository.ListRecentlyReceivedAsync(3, Token);

        Assert.Equal([2, 5], upcoming.Select(order => order.Id));
        Assert.Equal([5, 4, 2], recent.Select(order => order.Id));
        Assert.Equal("Luis Pérez", recent[0].TechnicianName);
    }
}
