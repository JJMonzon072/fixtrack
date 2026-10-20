namespace FixTrack.Domain.ServiceOrders;

/// <summary>Prioridad de atención de una orden. Se guarda como entero.</summary>
public enum ServiceOrderPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}
