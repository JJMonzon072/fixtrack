namespace FixTrack.Domain.ServiceOrders;

/// <summary>Reglas de plazos de las órdenes de servicio.</summary>
public static class ServiceOrderSchedule
{
    /// <summary>
    /// Una orden está atrasada cuando sigue abierta y su entrega comprometida ya pasó.
    /// Las órdenes sin fecha comprometida nunca están atrasadas.
    /// </summary>
    public static bool IsOverdue(ServiceOrderStatus status, DateTime? promisedAtUtc, DateTime nowUtc) =>
        ServiceOrderWorkflow.IsOpen(status) && promisedAtUtc is { } promised && promised < nowUtc;
}
