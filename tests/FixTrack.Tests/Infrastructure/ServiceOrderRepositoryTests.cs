using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Consultas de órdenes sobre SQLite en memoria: filtros, conteos, orden y paginación en SQL.</summary>
public sealed class ServiceOrderRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Cinco órdenes, insertadas en este orden (Id 1 a 5):
    /// 1 Ana · Dell · Alta · Carla · En reparación · vencida;
    /// 2 Mario · HP · Normal · sin técnico · Recibida · entrega mañana;
    /// 3 Ana · Dell · Baja · Carla · Entregada;
    /// 4 Mario · HP · Urgente · Luis · Cancelada;
    /// 5 Ana · Lenovo · Urgente · Luis · En diagnóstico · entrega en 5 días.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var context = _database.CreateContext();
        var ana = new Customer("Ana Morales", "5555-0001", null, null, Now.AddDays(-60));
        var mario = new Customer("Mario Castillo", "5555-0002", null, null, Now.AddDays(-60));
        var carla = new Technician("Carla Ruiz", "Laptops", null, null, Now.AddDays(-90));
        var luis = new Technician("Luis Pérez", "Impresoras", null, null, Now.AddDays(-90));
        context.AddRange(ana, mario, carla, luis);
        await context.SaveChangesAsync(Token);

        var dell = ana.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", "SN-DELL-1", null, Now.AddDays(-60));
        var lenovo = ana.RegisterDevice(DeviceType.Desktop, "Lenovo", "ThinkCentre", null, null, Now.AddDays(-60));
        var hp = mario.RegisterDevice(DeviceType.Printer, "HP", "LaserJet", "SN-HP-9", null, Now.AddDays(-60));
        await context.SaveChangesAsync(Token);

        var inRepair = new ServiceOrder(dell, "No enciende", ServiceOrderPriority.High, Now.AddDays(-5), Now.AddDays(-1), 400m, null, carla);
        inRepair.ChangeStatus(ServiceOrderStatus.Diagnosing, Now.AddDays(-4));
        inRepair.RecordDiagnosis("Fuente dañada", Now.AddDays(-4));
        inRepair.ChangeStatus(ServiceOrderStatus.InRepair, Now.AddDays(-3));

        var received = new ServiceOrder(hp, "Atasco de papel", ServiceOrderPriority.Normal, Now.AddDays(-2), Now.AddDays(1), null, null);

        var delivered = new ServiceOrder(dell, "Teclado", ServiceOrderPriority.Low, Now.AddDays(-10), Now.AddDays(-8), 150m, null, carla);
        delivered.ChangeStatus(ServiceOrderStatus.Diagnosing, Now.AddDays(-10));
        delivered.RecordDiagnosis("Teclado con derrame", Now.AddDays(-10));
        delivered.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Now.AddDays(-9));
        delivered.ChangeStatus(ServiceOrderStatus.Delivered, Now.AddDays(-7), "Pagado", 175m);

        var cancelled = new ServiceOrder(hp, "No imprime", ServiceOrderPriority.Urgent, Now.AddDays(-1), null, null, null, luis);
        cancelled.ChangeStatus(ServiceOrderStatus.Cancelled, Now.AddHours(-20), "Duplicada");

        var diagnosing = new ServiceOrder(lenovo, "Lento", ServiceOrderPriority.Urgent, Now.AddHours(-3), Now.AddDays(5), null, null, luis);
        diagnosing.ChangeStatus(ServiceOrderStatus.Diagnosing, Now.AddHours(-2));

        foreach (var order in new[] { inRepair, received, delivered, cancelled, diagnosing })
        {
            context.ServiceOrders.Add(order);
            await context.SaveChangesAsync(Token);
        }
    }

    private async Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search)
    {
        await using var context = _database.CreateContext();
        return await new ServiceOrderRepository(context).SearchAsync(search, Now, Token);
    }

    private static int[] Ids(ServiceOrderSearchResult result) => [.. result.Page.Items.Select(order => order.Id)];

    [Fact]
    public async Task Open_orders_are_sorted_by_promised_date_and_counted_per_view()
    {
        await SeedAsync();

        var result = await SearchAsync(new ServiceOrderSearch(null));

        Assert.Equal([1, 2, 5], Ids(result));
        Assert.Equal(new ServiceOrderViewCounts(Open: 3, Overdue: 1, Closed: 2), result.Counts);
        Assert.Equal(5, result.Counts.All);
        Assert.Equal(3, result.Page.TotalCount);
    }

    [Fact]
    public async Task List_items_carry_their_customer_device_and_technician()
    {
        await SeedAsync();

        var first = (await SearchAsync(new ServiceOrderSearch(null))).Page.Items[0];

        Assert.Equal("OS-000001", first.Number);
        Assert.Equal("Ana Morales", first.CustomerName);
        Assert.Equal(("Dell", "Latitude 5440", DeviceType.Laptop), (first.DeviceBrand, first.DeviceModel, first.DeviceType));
        Assert.Equal("Carla Ruiz", first.TechnicianName);
        Assert.True(first.IsOverdueAt(Now));
    }

    [Fact]
    public async Task The_overdue_view_only_includes_open_orders_past_their_promised_date()
    {
        await SeedAsync();

        var overdue = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.Overdue));

        Assert.Equal([1], Ids(overdue));
    }

    [Fact]
    public async Task Closed_and_complete_listings_are_sorted_from_newest_to_oldest()
    {
        await SeedAsync();

        var closed = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.Closed));
        var all = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All));

        Assert.Equal([4, 3], Ids(closed));
        Assert.Equal([5, 4, 2, 1, 3], Ids(all));
    }

    [Fact]
    public async Task Filters_by_status_priority_and_technician()
    {
        await SeedAsync();

        var inRepair = await SearchAsync(new ServiceOrderSearch(null, Status: ServiceOrderStatus.InRepair));
        var urgent = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, Priority: ServiceOrderPriority.Urgent));
        var carla = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, TechnicianId: 1));
        var unassigned = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, UnassignedOnly: true));

        Assert.Equal([1], Ids(inRepair));
        Assert.Equal(1, inRepair.Page.TotalCount);
        Assert.Equal([5, 4], Ids(urgent));
        Assert.Equal([1, 3], Ids(carla));
        Assert.Equal(new ServiceOrderViewCounts(1, 1, 1), carla.Counts);
        Assert.Equal([2], Ids(unassigned));
    }

    [Theory]
    [InlineData("OS-000002", new[] { 2 })]
    [InlineData("os 5", new[] { 5 })]
    [InlineData("#3", new[] { 3 })]
    [InlineData("mario", new[] { 4, 2 })]
    [InlineData("dell latitude", new[] { 1, 3 })]
    [InlineData("thinkcentre", new[] { 5 })]
    [InlineData("sn-hp", new[] { 4, 2 })]
    [InlineData("carla", new[] { 1, 3 })]
    [InlineData("100%", new int[0])]
    public async Task Searches_by_number_customer_device_or_technician(string text, int[] expected)
    {
        await SeedAsync();

        var result = await SearchAsync(new ServiceOrderSearch(text, ServiceOrderView.All));

        Assert.Equal(expected, Ids(result));
    }

    [Fact]
    public async Task A_page_beyond_the_last_one_shows_the_last_page()
    {
        await SeedAsync();

        var result = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, Page: 9));

        Assert.Equal(1, result.Page.Page);
        Assert.Equal(5, result.Page.Items.Count);
    }

    [Fact]
    public async Task An_empty_database_returns_zero_counts()
    {
        var result = await SearchAsync(new ServiceOrderSearch(null));

        Assert.Empty(result.Page.Items);
        Assert.Equal(new ServiceOrderViewCounts(0, 0, 0), result.Counts);
    }

    [Fact]
    public async Task Details_include_the_related_records_and_the_history_in_chronological_order()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var details = await new ServiceOrderRepository(context).GetDetailsAsync(3, Token);

        Assert.NotNull(details);
        Assert.Equal("OS-000003", details.Number);
        Assert.Equal("Ana Morales", details.Customer.FullName);
        Assert.Equal("SN-DELL-1", details.Device.SerialNumber);
        Assert.Equal("Carla Ruiz", details.Technician!.FullName);
        Assert.Equal(Now.AddDays(-10), details.Technician.AssignedAtUtc);
        Assert.Equal(175m, details.FinalCost);
        Assert.Equal(25m, details.CostVariance);
        Assert.Equal(
            [ServiceOrderStatus.Received, ServiceOrderStatus.Diagnosing, ServiceOrderStatus.ReadyForPickup, ServiceOrderStatus.Delivered],
            details.History.Select(entry => entry.ToStatus));
        Assert.Equal("Pagado", details.History[^1].Note);
        Assert.True(details.IsClosed);
        Assert.Null(await new ServiceOrderRepository(context).GetDetailsAsync(99, Token));
    }

    [Fact]
    public async Task Filters_by_customer_and_device()
    {
        await SeedAsync();

        var mario = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, CustomerId: 2));
        var lenovo = await SearchAsync(new ServiceOrderSearch(null, ServiceOrderView.All, DeviceId: 2));

        Assert.Equal([4, 2], Ids(mario));
        Assert.Equal([5], Ids(lenovo));
    }

    [Fact]
    public async Task Customer_orders_are_counted_and_listed_from_the_newest()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();
        var repository = new ServiceOrderRepository(context);

        var ana = await repository.ListByCustomerAsync(1, take: 2, Token);
        var nobody = await repository.ListByCustomerAsync(99, take: 2, Token);

        Assert.Equal((3, 2), (ana.TotalCount, ana.OpenCount));
        Assert.Equal([5, 1], ana.Recent.Select(order => order.Id));
        Assert.Equal(("Lenovo", "ThinkCentre", "Ana Morales", "Luis Pérez"), (ana.Recent[0].DeviceBrand, ana.Recent[0].DeviceModel, ana.Recent[0].CustomerName, ana.Recent[0].TechnicianName));
        Assert.Equal((0, 0), (nobody.TotalCount, nobody.OpenCount));
        Assert.Empty(nobody.Recent);
    }

    [Fact]
    public async Task Workload_counts_only_open_orders_per_technician()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var workload = await new TechnicianRepository(context).ListWorkloadAsync(Token);

        Assert.Equal(["Carla Ruiz", "Luis Pérez"], workload.Select(t => t.FullName));
        Assert.Equal([1, 1], workload.Select(t => t.OpenOrderCount));
    }
}
