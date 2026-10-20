using FixTrack.Domain.Common;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Domain;

public sealed class ServiceOrderTests
{
    private static readonly DateTime Received = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private static ServiceOrder NewOrder() =>
        new(deviceId: 1, "No enciende", ServiceOrderPriority.Normal, Received, Received.AddDays(3), estimatedCost: 350.50m);

    [Fact]
    public void A_new_order_starts_received_without_technician_and_with_its_first_history_entry()
    {
        var order = NewOrder();

        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Null(order.TechnicianId);
        var entry = Assert.Single(order.StatusHistory);
        Assert.Null(entry.FromStatus);
        Assert.Equal(ServiceOrderStatus.Received, entry.ToStatus);
        Assert.Equal(Received, entry.ChangedAtUtc);
    }

    [Fact]
    public void Every_status_change_is_recorded_in_order()
    {
        var order = NewOrder();

        order.ChangeStatus(ServiceOrderStatus.Diagnosing, Received.AddHours(1));
        order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddHours(2), "Se cambia la fuente de poder");

        Assert.Equal(ServiceOrderStatus.InRepair, order.Status);
        Assert.Equal(
            [ServiceOrderStatus.Received, ServiceOrderStatus.Diagnosing, ServiceOrderStatus.InRepair],
            order.StatusHistory.Select(entry => entry.ToStatus));
        Assert.Equal(ServiceOrderStatus.Diagnosing, order.StatusHistory.Last().FromStatus);
    }

    [Fact]
    public void Delivery_requires_the_order_to_be_ready_and_closes_it()
    {
        var order = NewOrder();
        Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddDays(1)));

        order.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Received.AddDays(1));
        order.ChangeStatus(ServiceOrderStatus.Delivered, Received.AddDays(2), finalCost: 400m);

        Assert.True(order.IsClosed);
        Assert.Equal(Received.AddDays(2), order.DeliveredAtUtc);
        Assert.Equal(400m, order.FinalCost);
        Assert.Throws<DomainException>(() => order.ChangeStatus(ServiceOrderStatus.InRepair, Received.AddDays(3)));
        Assert.Throws<DomainException>(() => order.AssignTechnician(1, Received.AddDays(3)));
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Received)]
    public void Invalid_transitions_are_rejected(ServiceOrderStatus target)
    {
        var order = NewOrder();
        Assert.Throws<DomainException>(() => order.ChangeStatus(target, Received.AddHours(1)));
    }

    [Fact]
    public void A_technician_can_be_assigned_later()
    {
        var order = NewOrder();

        order.AssignTechnician(technicianId: 7, Received.AddHours(1));

        Assert.Equal(7, order.TechnicianId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.555)]
    [InlineData(100_000_000)]
    public void Amounts_must_be_non_negative_with_two_decimals(double amount) =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(1, "No enciende", ServiceOrderPriority.Low, Received, null, (decimal)amount));

    [Fact]
    public void The_promised_date_cannot_be_before_reception() =>
        Assert.Throws<DomainException>(() =>
            new ServiceOrder(1, "No enciende", ServiceOrderPriority.High, Received, Received.AddDays(-1), null));
}
