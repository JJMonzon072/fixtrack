using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Auditing;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

/// <summary>Casos de uso de órdenes de servicio con repositorios en memoria: sin base de datos.</summary>
public sealed class ServiceOrderServiceTests
{
    private readonly FakeAuditLog _audit = new();

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FakeServiceOrderRepository _orders;
    private readonly IServiceOrderService _service;
    private readonly Customer _customer;
    private readonly Device _device;
    private readonly Technician _technician;

    public ServiceOrderServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_store);
        _orders = new FakeServiceOrderRepository(_store);
        _service = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IServiceOrderRepository>(_orders)
            .AddSingleton<IDeviceRepository>(new FakeDeviceRepository(_store))
            .AddSingleton<ITechnicianRepository>(new FakeTechnicianRepository(_store))
            .AddSingleton<ICustomerRepository>(new FakeCustomerRepository(_store))
            .AddSingleton<IUnitOfWork>(_unitOfWork)
            .AddSingleton<ICurrentUser>(FakeCurrentUser.Instance)
            .AddSingleton<IAuditLog>(_audit)
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .BuildServiceProvider()
            .GetRequiredService<IServiceOrderService>();

        _customer = AddCustomer("Ana Morales");
        _device = AddDevice(_customer, "Dell");
        _technician = AddTechnician("Luis Pérez");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTime UtcNow => Now.UtcDateTime;

    private Customer AddCustomer(string name)
    {
        var customer = new Customer(name, "5555-1234", null, null, UtcNow.AddDays(-30));
        InMemoryStore.AssignId(customer, _store.Customers.Count + 1);
        _store.Customers.Add(customer);
        return customer;
    }

    private Device AddDevice(Customer owner, string brand)
    {
        var device = owner.RegisterDevice(DeviceType.Laptop, brand, null, null, null, UtcNow.AddDays(-30));
        InMemoryStore.AssignId(device, _store.Devices.Count + 1);
        _store.Devices.Add(device);
        return device;
    }

    private Technician AddTechnician(string name, bool active = true)
    {
        var technician = new Technician(name, "Laptops", null, null, UtcNow.AddDays(-60));
        InMemoryStore.AssignId(technician, _store.Technicians.Count + 1);
        if (!active)
        {
            technician.Deactivate(UtcNow.AddDays(-1));
        }

        _store.Technicians.Add(technician);
        return technician;
    }

    private ServiceOrderInput Input(int? technicianId = null) => new(
        _customer.Id, _device.Id, "No enciende", ServiceOrderPriority.High, technicianId, UtcNow.AddDays(2), 350.50m, "Con cargador");

    private async Task<ServiceOrder> CreateOrderAsync(int? technicianId = null)
    {
        var result = await _service.CreateAsync(Input(technicianId), Token);
        Assert.True(result.Succeeded);
        return _store.ServiceOrders.Single(order => order.Id == result.Value);
    }

    private async Task<ServiceOrder> DiagnosedOrderAsync()
    {
        var order = await CreateOrderAsync(_technician.Id);
        Assert.True((await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Diagnosing, null, null), order.Version, Token)).Succeeded);
        Assert.True((await _service.RecordDiagnosisAsync(order.Id, "Fuente dañada", order.Version, Token)).Succeeded);
        return order;
    }

    [Fact]
    public async Task Registers_a_received_order_and_locks_the_technician_inside_a_transaction()
    {
        var result = await _service.CreateAsync(Input(_technician.Id), Token);

        Assert.True(result.Succeeded);
        var order = Assert.Single(_store.ServiceOrders);
        Assert.Equal(result.Value, order.Id);
        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Equal(UtcNow, order.ReceivedAtUtc);
        Assert.Same(_technician, order.Technician);
        Assert.Equal("Con cargador", order.IntakeNotes);
        Assert.Single(order.StatusHistory);
        Assert.Equal([_technician.Id], _store.LockedTechnicians);
        Assert.Equal(1, _unitOfWork.CommittedTransactions);
    }

    [Fact]
    public async Task Returns_every_validation_error_and_saves_nothing()
    {
        var result = await _service.CreateAsync(new ServiceOrderInput(null, null, " ", null, null, UtcNow.AddHours(-1), -1m, null), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Equal(
            ["CustomerId", "DeviceId", nameof(ServiceOrder.EstimatedCost), nameof(ServiceOrder.Priority), nameof(ServiceOrder.PromisedAtUtc), nameof(ServiceOrder.ReportedIssue)],
            result.Errors.Keys.Order());
        Assert.Empty(_store.ServiceOrders);
        Assert.Equal(1, _unitOfWork.RolledBackTransactions);
    }

    [Fact]
    public async Task The_device_must_belong_to_the_selected_customer()
    {
        var other = AddCustomer("Carlos Méndez");

        var result = await _service.CreateAsync(Input() with { CustomerId = other.Id }, Token);

        Assert.Equal("El equipo seleccionado no pertenece a este cliente.", result.Errors["DeviceId"]);
        Assert.Empty(_store.ServiceOrders);
    }

    [Fact]
    public async Task Orders_cannot_be_registered_for_an_inactive_customer()
    {
        _customer.Deactivate(UtcNow.AddDays(-1));

        var result = await _service.CreateAsync(Input(), Token);

        Assert.Contains("inactivo", result.Errors["CustomerId"]);
        Assert.Empty(_store.ServiceOrders);
    }

    [Fact]
    public async Task An_inactive_or_unknown_technician_cannot_be_assigned_at_reception()
    {
        var inactive = AddTechnician("Marco Reyes", active: false);

        var withInactive = await _service.CreateAsync(Input(inactive.Id), Token);
        var withUnknown = await _service.CreateAsync(Input(99), Token);

        Assert.Equal("El técnico seleccionado está inactivo y no puede recibir asignaciones.", withInactive.Errors["TechnicianId"]);
        Assert.Equal("Selecciona un técnico de la lista.", withUnknown.Errors["TechnicianId"]);
        Assert.Empty(_store.ServiceOrders);
    }

    [Fact]
    public async Task Assigns_and_reassigns_the_technician_with_the_technician_locked()
    {
        var order = await CreateOrderAsync();
        var second = AddTechnician("Andrea Castillo");

        var first = await _service.AssignTechnicianAsync(order.Id, _technician.Id, null, version: 0, Token);
        var reassigned = await _service.AssignTechnicianAsync(order.Id, second.Id, null, version: 1, Token);

        Assert.True(first.Succeeded);
        Assert.True(reassigned.Succeeded);
        Assert.Same(second, order.Technician);
        Assert.Equal(2, order.Version);
        Assert.Equal([_technician.Id, second.Id], _store.LockedTechnicians);
    }

    [Fact]
    public async Task A_reassignment_records_who_made_it_and_why()
    {
        var order = await CreateOrderAsync(_technician.Id);
        var second = AddTechnician("Andrea Castillo");

        var result = await _service.AssignTechnicianAsync(order.Id, second.Id, "Luis tiene demasiadas órdenes", order.Version, Token);

        Assert.True(result.Succeeded);
        var reassignment = order.Assignments.Last();
        Assert.Equal((_technician.Id, second.Id), (reassignment.FromTechnicianId, reassignment.ToTechnicianId));
        Assert.Equal((FakeCurrentUser.Instance.Actor.UserId, "Ana Recepción"), (reassignment.AssignedByUserId, reassignment.AssignedByName));
        Assert.Equal("Luis tiene demasiadas órdenes", reassignment.Note);
        Assert.Equal("Ana Recepción", order.StatusHistory.First().ChangedByName);
    }

    [Fact]
    public async Task An_overlong_reassignment_reason_is_reported_on_its_field()
    {
        var order = await CreateOrderAsync(_technician.Id);
        var second = AddTechnician("Andrea Castillo");

        var result = await _service.AssignTechnicianAsync(order.Id, second.Id, new string('x', 201), order.Version, Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains("200", result.Errors["Note"]);
        Assert.Same(_technician, order.Technician);
    }

    [Fact]
    public async Task Rejects_inactive_and_already_assigned_technicians()
    {
        var order = await CreateOrderAsync(_technician.Id);
        var inactive = AddTechnician("Marco Reyes", active: false);

        var same = await _service.AssignTechnicianAsync(order.Id, _technician.Id, null, order.Version, Token);
        var notActive = await _service.AssignTechnicianAsync(order.Id, inactive.Id, null, order.Version, Token);
        var missing = await _service.AssignTechnicianAsync(order.Id, null, null, order.Version, Token);

        Assert.Equal("La orden ya está asignada a este técnico.", same.Errors["TechnicianId"]);
        Assert.Contains("inactivo", notActive.Errors["TechnicianId"]);
        Assert.Equal("Selecciona el técnico responsable.", missing.Errors["TechnicianId"]);
        Assert.Same(_technician, order.Technician);
    }

    [Fact]
    public async Task A_stale_version_is_rejected_without_changes()
    {
        var order = await CreateOrderAsync();
        await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Urgent, null, null), version: 0, Token);

        var result = await _service.AssignTechnicianAsync(order.Id, _technician.Id, null, version: 0, Token);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Equal(ServiceOrderService.StaleMessage, result.Message);
        Assert.Null(order.TechnicianId);
    }

    [Fact]
    public async Task A_concurrent_save_is_reported_as_a_conflict()
    {
        var order = await CreateOrderAsync();
        _unitOfWork.FailNextSaveWithConcurrencyConflict = true;

        var result = await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Low, null, null), order.Version, Token);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Equal(ServiceOrderService.StaleMessage, result.Message);
    }

    [Fact]
    public async Task Unknown_orders_are_not_found()
    {
        Assert.Equal(CommandStatus.NotFound, (await _service.AssignTechnicianAsync(42, _technician.Id, null, 0, Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.RecordDiagnosisAsync(42, "x", 0, Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.ChangeStatusAsync(0, new(ServiceOrderStatus.Diagnosing, null, null), 0, Token)).Status);
        Assert.Null(await _service.GetAsync(0, Token));
    }

    [Fact]
    public async Task The_diagnosis_is_only_recorded_while_the_order_is_on_the_bench()
    {
        var order = await CreateOrderAsync(_technician.Id);

        var tooEarly = await _service.RecordDiagnosisAsync(order.Id, "Fuente dañada", order.Version, Token);
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Diagnosing, null, null), order.Version, Token);
        var empty = await _service.RecordDiagnosisAsync(order.Id, "   ", order.Version, Token);
        var recorded = await _service.RecordDiagnosisAsync(order.Id, " Fuente dañada ", order.Version, Token);

        Assert.Contains("en diagnóstico", tooEarly.Errors["Diagnosis"]);
        Assert.Equal("Describe el diagnóstico técnico.", empty.Errors["Diagnosis"]);
        Assert.True(recorded.Succeeded);
        Assert.Equal("Fuente dañada", order.Diagnosis);
    }

    [Fact]
    public async Task Updates_the_plan_and_reports_problems_by_field()
    {
        var order = await CreateOrderAsync();

        var invalid = await _service.UpdatePlanAsync(order.Id, new(null, UtcNow.AddHours(-2), 10.555m), order.Version, Token);
        var valid = await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Urgent, UtcNow.AddDays(1), 500m), order.Version, Token);

        Assert.Equal([nameof(ServiceOrder.EstimatedCost), nameof(ServiceOrder.Priority), nameof(ServiceOrder.PromisedAtUtc)], invalid.Errors.Keys.Order());
        Assert.True(valid.Succeeded);
        Assert.Equal(ServiceOrderPriority.Urgent, order.Priority);
        Assert.Equal(500m, order.EstimatedCost);
    }

    [Fact]
    public async Task Rejected_transitions_are_reported_on_the_status_field()
    {
        var order = await CreateOrderAsync(_technician.Id);

        var skip = await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Delivered, null, 100m), order.Version, Token);
        var missing = await _service.ChangeStatusAsync(order.Id, new(null, null, null), order.Version, Token);
        var cancelWithoutReason = await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Cancelled, " ", null), order.Version, Token);

        Assert.Equal("Una orden «Recibida» no puede pasar a «Entregada».", skip.Errors["Status"]);
        Assert.Equal("Selecciona el nuevo estado.", missing.Errors["Status"]);
        Assert.Equal("Indica el motivo de la cancelación.", cancelWithoutReason.Errors["Note"]);
        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Single(order.StatusHistory);
    }

    [Fact]
    public async Task Delivers_a_ready_order_with_its_final_cost()
    {
        var order = await DiagnosedOrderAsync();
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.ReadyForPickup, "Probada", null), order.Version, Token);

        var withoutCost = await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Delivered, null, null), order.Version, Token);
        var delivered = await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Delivered, "Pagado en efectivo", 380m), order.Version, Token);

        Assert.Equal("Indica el costo final para entregar la orden.", withoutCost.Errors["FinalCost"]);
        Assert.True(delivered.Succeeded);
        Assert.True(order.IsClosed);
        Assert.Equal(380m, order.FinalCost);
        Assert.Equal(29.50m, order.CostVariance);
        Assert.Equal(ServiceOrderStatus.Delivered, order.StatusHistory.Last().ToStatus);
    }

    [Fact]
    public async Task Each_important_operation_leaves_one_audit_event()
    {
        var order = await CreateOrderAsync();
        var second = AddTechnician("Andrea Castillo");
        await _service.AssignTechnicianAsync(order.Id, _technician.Id, null, order.Version, Token);
        await _service.AssignTechnicianAsync(order.Id, second.Id, "Luis está de vacaciones", order.Version, Token);
        await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Urgent, order.PromisedAtUtc, 500m), order.Version, Token);
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Diagnosing, null, null), order.Version, Token);
        await _service.RecordDiagnosisAsync(order.Id, "Fuente dañada", order.Version, Token);
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Cancelled, "El cliente desistió", null), order.Version, Token);

        Assert.All(_audit.Events, entry => Assert.Equal(("orden", order.Id.ToString()), (entry.EntityType, entry.EntityId)));
        Assert.Equal(
            [
                AuditActions.OrderRegistered,
                AuditActions.TechnicianAssigned,
                AuditActions.TechnicianReassigned,
                AuditActions.PlanChanged,
                AuditActions.StatusChanged,
                AuditActions.DiagnosisRecorded,
                AuditActions.OrderCancelled,
            ],
            _audit.Events.Select(entry => entry.Action));
        var summaries = _audit.Events.Select(entry => entry.Summary).ToList();
        Assert.StartsWith($"{order.Number}: registrada para Dell", summaries[0]);
        Assert.Contains("Reasignada de Luis Pérez a Andrea Castillo. Motivo: Luis está de vacaciones", summaries[2]);
        Assert.Contains("Prioridad Alta → Urgente; costo estimado Q 350.50 → Q 500.00", summaries[3]);
        Assert.Contains("Recibida → En diagnóstico.", summaries[4]);
        Assert.Contains("Cancelada desde En diagnóstico. Motivo: El cliente desistió", summaries[6]);
    }

    [Fact]
    public async Task A_delivery_records_the_final_and_estimated_cost()
    {
        var order = await DiagnosedOrderAsync();
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.ReadyForPickup, null, null), order.Version, Token);

        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Delivered, null, 380m), order.Version, Token);

        var delivery = _audit.Events.Last();
        Assert.Equal(AuditActions.OrderDelivered, delivery.Action);
        Assert.Contains("Entregada con costo final Q 380.00 (estimado: Q 350.50)", delivery.Summary);
    }

    [Fact]
    public async Task Rejected_or_unchanged_operations_leave_no_audit_event()
    {
        var order = await CreateOrderAsync();
        var before = _audit.Events.Count;

        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Delivered, null, 100m), order.Version, Token);
        await _service.AssignTechnicianAsync(order.Id, null, null, order.Version, Token);
        await _service.UpdatePlanAsync(order.Id, new(order.Priority, order.PromisedAtUtc, order.EstimatedCost), order.Version, Token);
        await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Urgent, null, null), version: 99, Token);

        Assert.Equal(before, _audit.Events.Count);
    }

    [Fact]
    public async Task Closed_orders_reject_further_changes()
    {
        var order = await CreateOrderAsync();
        await _service.ChangeStatusAsync(order.Id, new(ServiceOrderStatus.Cancelled, "El cliente desistió", null), order.Version, Token);

        var result = await _service.UpdatePlanAsync(order.Id, new(ServiceOrderPriority.Urgent, null, null), order.Version, Token);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Equal(ServiceOrderService.ClosedMessage, result.Message);
    }

    [Theory]
    [InlineData(ServiceOrderView.Open, ServiceOrderStatus.Delivered, null)]
    [InlineData(ServiceOrderView.Overdue, ServiceOrderStatus.Cancelled, null)]
    [InlineData(ServiceOrderView.Closed, ServiceOrderStatus.InRepair, null)]
    [InlineData(ServiceOrderView.Closed, ServiceOrderStatus.Delivered, ServiceOrderStatus.Delivered)]
    [InlineData(ServiceOrderView.All, ServiceOrderStatus.InRepair, ServiceOrderStatus.InRepair)]
    public async Task Search_ignores_statuses_outside_the_selected_view(
        ServiceOrderView view,
        ServiceOrderStatus status,
        ServiceOrderStatus? expected)
    {
        await _service.SearchAsync(new ServiceOrderSearch(null, view, status), Token);

        Assert.Equal(expected, _orders.LastSearch!.Status);
    }

    [Fact]
    public async Task Search_normalizes_its_criteria_and_uses_the_current_time()
    {
        await _service.SearchAsync(
            new ServiceOrderSearch("  " + new string('x', 150) + " ", (ServiceOrderView)9, null, (ServiceOrderPriority)9, 5, UnassignedOnly: true, Page: -3),
            Token);

        var search = _orders.LastSearch!;
        Assert.Equal(ServiceOrderSearch.TextMaxLength, search.Text!.Length);
        Assert.Equal(ServiceOrderView.Open, search.View);
        Assert.Null(search.Priority);
        Assert.Null(search.TechnicianId);
        Assert.True(search.UnassignedOnly);
        Assert.Equal(1, search.Page);
        Assert.Equal(UtcNow, _orders.LastSearchNow);
    }
}
