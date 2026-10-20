using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Domain;

/// <summary>Flujo de estados de las órdenes: transiciones permitidas, estados abiertos y cerrados.</summary>
public sealed class ServiceOrderWorkflowTests
{
    [Theory]
    [InlineData(ServiceOrderStatus.Received, ServiceOrderStatus.Delivered)]
    [InlineData(ServiceOrderStatus.Received, ServiceOrderStatus.InRepair)]
    [InlineData(ServiceOrderStatus.Received, ServiceOrderStatus.ReadyForPickup)]
    [InlineData(ServiceOrderStatus.Diagnosing, ServiceOrderStatus.Delivered)]
    [InlineData(ServiceOrderStatus.InRepair, ServiceOrderStatus.Diagnosing)]
    [InlineData(ServiceOrderStatus.AwaitingParts, ServiceOrderStatus.ReadyForPickup)]
    [InlineData(ServiceOrderStatus.ReadyForPickup, ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Delivered, ServiceOrderStatus.InRepair)]
    [InlineData(ServiceOrderStatus.Cancelled, ServiceOrderStatus.Received)]
    public void Illogical_transitions_are_not_allowed(ServiceOrderStatus from, ServiceOrderStatus to) =>
        Assert.False(ServiceOrderWorkflow.CanTransition(from, to));

    [Fact]
    public void The_workflow_defines_every_allowed_transition()
    {
        var allowed = Enum.GetValues<ServiceOrderStatus>()
            .SelectMany(from => ServiceOrderWorkflow.NextStatuses(from).Select(to => $"{from}->{to}"))
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "Received->Diagnosing", "Received->Cancelled",
                "Diagnosing->InRepair", "Diagnosing->AwaitingParts", "Diagnosing->ReadyForPickup", "Diagnosing->Cancelled",
                "InRepair->AwaitingParts", "InRepair->ReadyForPickup", "InRepair->Cancelled",
                "AwaitingParts->InRepair", "AwaitingParts->Cancelled",
                "ReadyForPickup->Delivered", "ReadyForPickup->InRepair",
            },
            allowed);
        Assert.All(ServiceOrderWorkflow.ClosedStatuses, status => Assert.Empty(ServiceOrderWorkflow.NextStatuses(status)));
    }

    [Fact]
    public void Open_and_closed_statuses_cover_every_status_once()
    {
        Assert.Equal(
            Enum.GetValues<ServiceOrderStatus>().Order(),
            ServiceOrderWorkflow.OpenStatuses.Concat(ServiceOrderWorkflow.ClosedStatuses).Order());
        Assert.True(ServiceOrderWorkflow.IsOpen(ServiceOrderStatus.ReadyForPickup));
        Assert.True(ServiceOrderWorkflow.IsClosed(ServiceOrderStatus.Cancelled));
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Received, "Recibida")]
    [InlineData(ServiceOrderStatus.AwaitingParts, "En espera de repuestos")]
    [InlineData(ServiceOrderStatus.Cancelled, "Cancelada")]
    public void Statuses_have_spanish_names(ServiceOrderStatus status, string expected) =>
        Assert.Equal(expected, ServiceOrderWorkflow.Name(status));
}
