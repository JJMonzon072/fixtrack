using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Tests.Application;

namespace FixTrack.Tests.Domain;

public sealed class ServiceOrderTests
{
    private static readonly DateTime Received = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private static Device NewDevice(bool ownerIsActive = true)
    {
        var owner = new Customer("Ana Morales", "5555-1234", null, null, Received.AddDays(-30));
        var device = owner.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", "DL-01", null, Received.AddDays(-30));
        if (!ownerIsActive)
        {
            owner.Deactivate(Received.AddDays(-1));
        }

        return device;
    }

    private static Technician ActiveTechnician(int id = 1, string name = "Luis Pérez")
    {
        var technician = new Technician(name, "Laptops y equipos de escritorio", null, null, Received.AddDays(-60));
        InMemoryStore.AssignId(technician, id);
        return technician;
    }

    private static ServiceOrder NewOrder(Technician? technician = null) =>
        new(NewDevice(), "No enciende", ServiceOrderPriority.Normal, Received, Received.AddDays(3), 350.50m, null, technician);

    /// <summary>Orden en diagnóstico, con técnico y diagnóstico registrados.</summary>
    private static ServiceOrder DiagnosedOrder()
    {
        var order = NewOrder(ActiveTechnician());
        order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1));
        order.RecordDiagnosis("Fuente de poder dañada.", Received.AddHours(2));
        return order;
    }

    [Fact]
    public void A_new_order_starts_received_with_its_first_history_entry()
    {
        var device = NewDevice();

        var order = new ServiceOrder(device, "  No enciende\r\n\r\n\r\nni carga ", ServiceOrderPriority.High, Received, null, null, "  Con cargador ");

        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Same(device, order.Device);
        Assert.Equal("No enciende\n\nni carga", order.ReportedIssue);
        Assert.Equal("Con cargador", order.IntakeNotes);
        Assert.Null(order.TechnicianId);
        Assert.Equal(Received, order.CreatedAtUtc);
        Assert.Equal(0, order.Version);
        var entry = Assert.Single(order.StatusHistory);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ServiceOrderStatus.Received, entry.ToStatus);
        Assert.Equal(Received, entry.ChangedAtUtc);
    }

    [Fact]
    public void Orders_cannot_be_registered_for_an_inactive_customer()
    {
        var error = Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(ownerIsActive: false), "No enciende", ServiceOrderPriority.Normal, Received, null, null, null));

        Assert.Equal("No se pueden registrar órdenes para un cliente inactivo.", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void The_reported_issue_is_required(string? issue) =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(), issue, ServiceOrderPriority.Normal, Received, null, null, null));

    [Fact]
    public void Long_texts_are_rejected()
    {
        Assert.Throws<DomainException>(() => new ServiceOrder(
            NewDevice(), new string('a', ServiceOrderRules.ReportedIssueMaxLength + 1), ServiceOrderPriority.Normal, Received, null, null, null));
        Assert.Throws<DomainException>(() => new ServiceOrder(
            NewDevice(), "No enciende", ServiceOrderPriority.Normal, Received, null, null, new string('a', ServiceOrderRules.IntakeNotesMaxLength + 1)));
    }

    [Fact]
    public void The_priority_must_be_valid() =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(), "No enciende", (ServiceOrderPriority)9, Received, null, null, null));

    [Theory]
    [InlineData(-1)]
    [InlineData(-24)]
    public void The_promised_date_cannot_be_before_reception(int hours) =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(), "No enciende", ServiceOrderPriority.High, Received, Received.AddHours(hours), null, null));

    [Fact]
    public void The_promised_date_must_be_within_a_year_of_reception()
    {
        var error = Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(), "No enciende", ServiceOrderPriority.High, Received, Received.AddDays(366), null, null));

        Assert.Contains("365 días", error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.555)]
    [InlineData(100_000_000)]
    public void Amounts_must_be_non_negative_with_two_decimals(double amount) =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(NewDevice(), "No enciende", ServiceOrderPriority.Low, Received, null, (decimal)amount, null));

    [Fact]
    public void A_technician_can_be_assigned_at_reception()
    {
        var technician = ActiveTechnician();

        var order = NewOrder(technician);

        Assert.Same(technician, order.Technician);
        Assert.Equal(technician.Id, order.TechnicianId);
        Assert.Equal(Received, order.TechnicianAssignedAtUtc);
    }

    [Fact]
    public void An_inactive_technician_cannot_be_assigned_at_reception()
    {
        var technician = ActiveTechnician();
        technician.Deactivate(Received.AddDays(-1));

        Assert.Throws<DomainException>(() => NewOrder(technician));
    }

    [Fact]
    public void A_technician_can_be_assigned_and_reassigned_later()
    {
        var order = NewOrder();
        var first = ActiveTechnician(1, "Luis Pérez");
        var second = ActiveTechnician(2, "Andrea Castillo");

        order.AssignTechnician(first, Received.AddHours(1));
        order.AssignTechnician(second, Received.AddHours(2));

        Assert.Same(second, order.Technician);
        Assert.Equal(2, order.TechnicianId);
        Assert.Equal(Received.AddHours(2), order.TechnicianAssignedAtUtc);
        Assert.Equal(Received.AddHours(2), order.UpdatedAtUtc);
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Assigning_the_current_technician_again_is_rejected()
    {
        var technician = ActiveTechnician();
        var order = NewOrder(technician);

        var error = Assert.Throws<DomainException>(() => order.AssignTechnician(technician, Received.AddHours(1)));

        Assert.Equal("La orden ya está asignada a este técnico.", error.Message);
    }

    [Fact]
    public void An_inactive_technician_cannot_receive_new_assignments()
    {
        var order = NewOrder();
        var technician = ActiveTechnician();
        technician.Deactivate(Received);

        var error = Assert.Throws<DomainException>(() => order.AssignTechnician(technician, Received.AddHours(1)));

        Assert.Contains("inactivo", error.Message);
        Assert.Null(order.Technician);
    }

    [Fact]
    public void The_full_workflow_records_every_change_in_order()
    {
        var order = DiagnosedOrder();

        order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddHours(3));
        order.ChangeStatus(ServiceOrderStatus.AwaitingParts, Received.AddHours(4), "Fuente de 65 W pedida al proveedor");
        order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddDays(2));
        order.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Received.AddDays(2).AddHours(3), "Probada durante 2 horas");
        order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddDays(3), finalCost: 420m);

        Assert.Equal(
            [
                ServiceOrderStatus.Received, ServiceOrderStatus.Diagnosing, ServiceOrderStatus.InRepair, ServiceOrderStatus.AwaitingParts,
                ServiceOrderStatus.InRepair, ServiceOrderStatus.ReadyForPickup, ServiceOrderStatus.Delivered,
            ],
            order.StatusHistory.Select(entry => entry.ToStatus));
        Assert.All(order.StatusHistory.Skip(1).Zip(order.StatusHistory), pair => Assert.Equal(pair.Second.ToStatus, pair.First.FromStatus));
        Assert.Equal("Fuente de 65 W pedida al proveedor", order.StatusHistory.ElementAt(3).Note);
        Assert.True(order.IsClosed);
        Assert.Equal(Received.AddDays(3), order.DeliveredAtUtc);
        Assert.Equal(420m, order.FinalCost);
        Assert.Equal(69.50m, order.CostVariance);
    }

    [Fact]
    public void A_received_order_cannot_be_delivered_directly()
    {
        var order = NewOrder(ActiveTechnician());

        var error = Assert.Throws<DomainException>(() =>
            order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddHours(1), finalCost: 100m));

        Assert.Equal("Una orden «Recibida» no puede pasar a «Entregada».", error.Message);
        Assert.Single(order.StatusHistory);
    }

    [Fact]
    public void Diagnosis_requires_an_assigned_technician()
    {
        var order = NewOrder();

        var error = Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1)));

        Assert.Equal("Asigna un técnico antes de iniciar el diagnóstico.", error.Message);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.InRepair)]
    [InlineData(ServiceOrderStatus.ReadyForPickup)]
    public void Leaving_diagnosis_requires_a_recorded_diagnosis(ServiceOrderStatus target)
    {
        var order = NewOrder(ActiveTechnician());
        order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1));

        var error = Assert.Throws<DomainException>(() => order.ChangeStatus(target, Received.AddHours(2)));

        Assert.Equal("Registra el diagnóstico antes de continuar con la orden.", error.Message);
    }

    [Fact]
    public void Waiting_for_parts_requires_saying_which_part()
    {
        var order = DiagnosedOrder();

        var error = Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.AwaitingParts, Received.AddHours(3), "  "));

        Assert.Equal("Indica qué repuesto se está esperando.", error.Message);
    }

    [Fact]
    public void Cancelling_requires_a_reason_and_closes_the_order()
    {
        var order = NewOrder();
        Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Cancelled, Received.AddHours(1)));

        order.ChangeStatus(ServiceOrderStatus.Cancelled, Received.AddHours(1), "El cliente retiró el equipo sin reparar");

        Assert.True(order.IsClosed);
        Assert.Null(order.DeliveredAtUtc);
        Assert.Null(order.FinalCost);
        Assert.Equal("El cliente retiró el equipo sin reparar", order.StatusHistory.Last().Note);
    }

    [Fact]
    public void Delivery_requires_a_valid_final_cost()
    {
        var order = DiagnosedOrder();
        order.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Received.AddHours(3));

        Assert.Equal(
            "Indica el costo final para entregar la orden.",
            Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddHours(4))).Message);
        Assert.Equal(
            "El costo final no puede ser negativo.",
            Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddHours(4), finalCost: -1m)).Message);
        Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddHours(4), finalCost: 10.001m));
        Assert.Equal(ServiceOrderStatus.ReadyForPickup, order.Status);
    }

    [Fact]
    public void A_final_cost_is_only_recorded_on_delivery()
    {
        var order = DiagnosedOrder();

        var error = Assert.Throws<DomainException>(() =>
            order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddHours(3), finalCost: 100m));

        Assert.Equal("El costo final solo se registra al entregar la orden.", error.Message);
    }

    [Fact]
    public void A_ready_order_can_go_back_to_repair_before_delivery()
    {
        var order = DiagnosedOrder();
        order.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Received.AddHours(3));

        order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddHours(4), "Falló la prueba final");

        Assert.Equal(ServiceOrderStatus.InRepair, order.Status);
    }

    [Fact]
    public void A_status_change_cannot_be_dated_before_the_last_change()
    {
        var order = DiagnosedOrder();

        var error = Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddMinutes(30)));

        Assert.Contains("anterior al último cambio", error.Message);
    }

    [Fact]
    public void Closed_orders_reject_every_change()
    {
        var order = NewOrder(ActiveTechnician());
        order.ChangeStatus(ServiceOrderStatus.Cancelled, Received.AddHours(1), "Duplicada");
        var later = Received.AddDays(1);

        Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Diagnosing, later));
        Assert.Throws<DomainException>(() => order.AssignTechnician(ActiveTechnician(2, "Andrea Castillo"), later));
        Assert.Throws<DomainException>(() => order.RecordDiagnosis("Revisión", later));
        Assert.Throws<DomainException>(() => order.UpdatePlan(ServiceOrderPriority.Urgent, null, null, later));
        Assert.Equal(2, order.StatusHistory.Count);
    }

    [Fact]
    public void The_diagnosis_is_recorded_while_the_order_is_on_the_bench()
    {
        var order = NewOrder(ActiveTechnician());
        Assert.Equal(
            "Inicia el diagnóstico antes de registrarlo.",
            Assert.Throws<DomainException>(() => order.RecordDiagnosis("Revisión", Received.AddHours(1))).Message);

        order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1));
        order.RecordDiagnosis("  Disco duro con sectores dañados.  ", Received.AddHours(2));
        order.RecordDiagnosis("Disco duro con sectores dañados; se recomienda SSD.", Received.AddHours(3));

        Assert.Equal("Disco duro con sectores dañados; se recomienda SSD.", order.Diagnosis);
        Assert.Equal(Received.AddHours(3), order.UpdatedAtUtc);
    }

    [Fact]
    public void The_diagnosis_cannot_change_once_the_order_is_ready()
    {
        var order = DiagnosedOrder();
        order.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Received.AddHours(3));

        Assert.Throws<DomainException>(() => order.RecordDiagnosis("Otro diagnóstico", Received.AddHours(4)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_diagnosis_is_rejected(string diagnosis)
    {
        var order = NewOrder(ActiveTechnician());
        order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1));

        Assert.Throws<DomainException>(() => order.RecordDiagnosis(diagnosis, Received.AddHours(2)));
        Assert.Throws<DomainException>(() =>
            order.RecordDiagnosis(new string('a', ServiceOrderRules.DiagnosisMaxLength + 1), Received.AddHours(2)));
    }

    [Fact]
    public void The_plan_of_an_open_order_can_be_updated()
    {
        var order = NewOrder();

        order.UpdatePlan(ServiceOrderPriority.Urgent, Received.AddDays(1), 500m, Received.AddHours(1));

        Assert.Equal(ServiceOrderPriority.Urgent, order.Priority);
        Assert.Equal(Received.AddDays(1), order.PromisedAtUtc);
        Assert.Equal(500m, order.EstimatedCost);
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public void A_new_promised_date_cannot_be_in_the_past()
    {
        var order = NewOrder();
        var now = Received.AddDays(5);

        var error = Assert.Throws<DomainException>(() => order.UpdatePlan(ServiceOrderPriority.High, Received.AddDays(4), null, now));

        Assert.Equal("La nueva fecha de entrega comprometida no puede estar en el pasado.", error.Message);
    }

    [Fact]
    public void An_overdue_order_keeps_its_promised_date_when_other_values_change()
    {
        var order = NewOrder();
        var now = Received.AddDays(5);

        order.UpdatePlan(ServiceOrderPriority.Urgent, order.PromisedAtUtc, 350.50m, now);

        Assert.Equal(ServiceOrderPriority.Urgent, order.Priority);
        Assert.Equal(Received.AddDays(3), order.PromisedAtUtc);
    }

    [Fact]
    public void The_order_number_is_derived_from_the_id() =>
        Assert.Equal("OS-000042", ServiceOrder.FormatNumber(42));
}
