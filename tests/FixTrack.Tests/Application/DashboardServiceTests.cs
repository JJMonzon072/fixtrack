using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Dashboard;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

/// <summary>Panel y cola de trabajo con repositorios en memoria.</summary>
public sealed class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStore _store = new();
    private readonly FakeDashboardRepository _dashboard = new();
    private readonly ServiceProvider _services;

    public DashboardServiceTests()
    {
        _services = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IDashboardRepository>(_dashboard)
            .AddSingleton<IServiceOrderRepository>(new FakeServiceOrderRepository(_store))
            .AddSingleton<IDeviceRepository>(new FakeDeviceRepository(_store))
            .AddSingleton<ITechnicianRepository>(new FakeTechnicianRepository(_store))
            .AddSingleton<ICustomerRepository>(new FakeCustomerRepository(_store))
            .AddSingleton<IUnitOfWork>(new FakeUnitOfWork(_store))
            .AddSingleton<ICurrentUser>(FakeCurrentUser.Instance)
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTime UtcNow => Now.UtcDateTime;

    private Technician AddTechnician(string name, bool active = true)
    {
        var technician = new Technician(name, "Laptops", null, null, UtcNow.AddDays(-90));
        InMemoryStore.AssignId(technician, _store.Technicians.Count + 1);
        if (!active)
        {
            technician.Deactivate(UtcNow.AddDays(-1));
        }

        _store.Technicians.Add(technician);
        return technician;
    }

    /// <summary>Agrega una orden abierta y la lleva al estado indicado siguiendo el flujo.</summary>
    private ServiceOrder AddOrder(ServiceOrderPriority priority, ServiceOrderStatus status, double receivedDaysAgo, double? promisedInHours, Technician? technician)
    {
        var customer = new Customer($"Cliente {_store.ServiceOrders.Count + 1}", "5555-1234", null, null, UtcNow.AddDays(-60));
        var device = customer.RegisterDevice(DeviceType.Laptop, "Dell", null, null, null, UtcNow.AddDays(-60));
        var received = UtcNow.AddDays(-receivedDaysAgo);
        var order = new ServiceOrder(device, "Falla", priority, received, promisedInHours is { } hours ? UtcNow.AddHours(hours) : null, null, null, technician);
        var at = received.AddMinutes(1);
        if (status is not ServiceOrderStatus.Received)
        {
            order.ChangeStatus(ServiceOrderStatus.Diagnosing, at);
        }

        if (status is ServiceOrderStatus.InRepair or ServiceOrderStatus.AwaitingParts or ServiceOrderStatus.ReadyForPickup)
        {
            order.RecordDiagnosis("Diagnóstico", at);
            order.ChangeStatus(status == ServiceOrderStatus.ReadyForPickup ? ServiceOrderStatus.ReadyForPickup : ServiceOrderStatus.InRepair, at);
        }

        if (status == ServiceOrderStatus.AwaitingParts)
        {
            order.ChangeStatus(ServiceOrderStatus.AwaitingParts, at, "Repuesto pedido");
        }

        InMemoryStore.AssignId(order, _store.ServiceOrders.Count + 1);
        _store.ServiceOrders.Add(order);
        return order;
    }

    [Fact]
    public async Task Without_orders_the_dashboard_shows_zeros_and_empty_lists()
    {
        AddTechnician("Luis Pérez");
        AddTechnician("Marco Reyes", active: false);

        var summary = await _services.GetRequiredService<IDashboardService>().GetAsync(Token);

        Assert.Equal(0, summary.Counts.Total);
        Assert.Empty(summary.RecommendedActions);
        Assert.Empty(summary.UpcomingDeliveries);
        Assert.Empty(summary.RecentlyReceived);
        Assert.Equal(["Luis Pérez"], summary.Workload.Select(t => t.FullName));
        Assert.Equal(UtcNow, summary.CalculatedAtUtc);
        Assert.Equal(7, summary.RecentDays);
    }

    [Fact]
    public async Task The_work_queue_separates_repairs_parts_and_deliveries_and_ranks_each_list()
    {
        var luis = AddTechnician("Luis Pérez");
        var low = AddOrder(ServiceOrderPriority.Low, ServiceOrderStatus.Received, 1, null, null);
        var urgent = AddOrder(ServiceOrderPriority.Urgent, ServiceOrderStatus.InRepair, 2, -1, luis);
        var normal = AddOrder(ServiceOrderPriority.Normal, ServiceOrderStatus.Diagnosing, 10, 30, luis);
        var parts = AddOrder(ServiceOrderPriority.Urgent, ServiceOrderStatus.AwaitingParts, 3, null, luis);
        var ready = AddOrder(ServiceOrderPriority.Normal, ServiceOrderStatus.ReadyForPickup, 4, -2, luis);

        var queue = await _services.GetRequiredService<IServiceOrderService>().GetWorkQueueAsync(Token);

        Assert.Equal([urgent.Id, normal.Id, low.Id], queue.Repair.Select(item => item.Order.Id));
        Assert.Equal([1, 2, 3], queue.Repair.Select(item => item.Position));
        Assert.Equal([parts.Id], queue.AwaitingParts.Select(item => item.Order.Id));
        Assert.Equal([ready.Id], queue.ReadyForPickup.Select(item => item.Order.Id));
        Assert.Equal(90 + 50 + 4, queue.Repair[0].Score.Total);
        Assert.Equal(30 + 15 + 20, queue.Repair[1].Score.Total);
        Assert.Equal(RecommendedAction.AssignTechnician, queue.Repair[2].Score.Action);
        Assert.False(queue.IsTruncated);
        Assert.Equal(5, queue.Count);
    }

    [Fact]
    public async Task Recommended_actions_combine_every_list_with_the_same_ranking()
    {
        var luis = AddTechnician("Luis Pérez");
        AddOrder(ServiceOrderPriority.Low, ServiceOrderStatus.Received, 0, null, luis);
        var ready = AddOrder(ServiceOrderPriority.High, ServiceOrderStatus.ReadyForPickup, 1, -5, luis);
        var repair = AddOrder(ServiceOrderPriority.Urgent, ServiceOrderStatus.InRepair, 1, 48, luis);
        _dashboard.Counts = OrderCounts.Empty with { Received = 1, InRepair = 1, ReadyForPickup = 1 };

        var summary = await _services.GetRequiredService<IDashboardService>().GetAsync(Token);

        // Lista para entrega y vencida: 60 + 50 + 2 = 112; urgente en reparación con entrega en 48 h: 90 + 15 + 2 = 107.
        Assert.Equal([ready.Id, repair.Id], summary.RecommendedActions.Take(2).Select(item => item.Order.Id));
        Assert.Equal([112, 107, 10], summary.RecommendedActions.Select(item => item.Score.Total));
        Assert.Equal(RecommendedAction.DeliverToCustomer, summary.RecommendedActions[0].Score.Action);
        Assert.Equal(_dashboard.LastNow, UtcNow);
        Assert.Equal(UtcNow.AddDays(-7), _dashboard.LastRecentSince);
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public OrderCounts Counts { get; set; } = OrderCounts.Empty;

        public DateTime? LastNow { get; private set; }

        public DateTime? LastRecentSince { get; private set; }

        public Task<OrderCounts> CountOrdersAsync(DateTime nowUtc, DateTime recentSinceUtc, CancellationToken cancellationToken)
        {
            LastNow = nowUtc;
            LastRecentSince = recentSinceUtc;
            return Task.FromResult(Counts);
        }

        public Task<IReadOnlyList<ServiceOrderSummary>> ListUpcomingDeliveriesAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceOrderSummary>>([]);

        public Task<IReadOnlyList<ServiceOrderSummary>> ListRecentlyReceivedAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceOrderSummary>>([]);
    }
}
