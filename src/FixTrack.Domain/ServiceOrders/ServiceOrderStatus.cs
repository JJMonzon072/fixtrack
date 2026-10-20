namespace FixTrack.Domain.ServiceOrders;

/// <summary>Estados de una orden de servicio. Se guardan como entero.</summary>
public enum ServiceOrderStatus
{
    Received = 1,
    Diagnosing = 2,
    InRepair = 3,
    AwaitingParts = 4,
    ReadyForPickup = 5,
    Delivered = 6,
    Cancelled = 7,
}
