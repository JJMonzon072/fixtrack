namespace FixTrack.Domain.ServiceOrders;

/// <summary>
/// Flujo de estados de una orden de servicio: qué transiciones están permitidas y qué estados
/// cuentan como abiertos. Una orden entregada o cancelada queda cerrada y ya no cambia.
/// </summary>
/// <remarks>
/// <code>
/// Recibida ──► En diagnóstico ──► En reparación ◄──► En espera de repuestos
///                   │                   │
///                   └──────────► Lista para entrega ──► Entregada
///                                       │
///                                       └──► En reparación (reapertura antes de entregar)
/// Cualquier estado abierto, excepto Lista para entrega, puede pasar a Cancelada.
/// </code>
/// </remarks>
public static class ServiceOrderWorkflow
{
    private static readonly Dictionary<ServiceOrderStatus, ServiceOrderStatus[]> Transitions = new()
    {
        [ServiceOrderStatus.Received] = [ServiceOrderStatus.Diagnosing, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.Diagnosing] =
        [
            ServiceOrderStatus.InRepair,
            ServiceOrderStatus.AwaitingParts,
            ServiceOrderStatus.ReadyForPickup,
            ServiceOrderStatus.Cancelled,
        ],
        [ServiceOrderStatus.InRepair] = [ServiceOrderStatus.AwaitingParts, ServiceOrderStatus.ReadyForPickup, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.AwaitingParts] = [ServiceOrderStatus.InRepair, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.ReadyForPickup] = [ServiceOrderStatus.Delivered, ServiceOrderStatus.InRepair],
        [ServiceOrderStatus.Delivered] = [],
        [ServiceOrderStatus.Cancelled] = [],
    };

    /// <summary>Estados en los que la orden sigue en el taller y admite cambios.</summary>
    public static IReadOnlyList<ServiceOrderStatus> OpenStatuses { get; } =
    [
        ServiceOrderStatus.Received,
        ServiceOrderStatus.Diagnosing,
        ServiceOrderStatus.InRepair,
        ServiceOrderStatus.AwaitingParts,
        ServiceOrderStatus.ReadyForPickup,
    ];

    /// <summary>Estados finales: la orden se entregó o se canceló.</summary>
    public static IReadOnlyList<ServiceOrderStatus> ClosedStatuses { get; } = [ServiceOrderStatus.Delivered, ServiceOrderStatus.Cancelled];

    /// <summary>Estados en los que se puede registrar o actualizar el diagnóstico.</summary>
    public static IReadOnlyList<ServiceOrderStatus> DiagnosisStatuses { get; } =
    [
        ServiceOrderStatus.Diagnosing,
        ServiceOrderStatus.InRepair,
        ServiceOrderStatus.AwaitingParts,
    ];

    public static bool IsOpen(ServiceOrderStatus status) => OpenStatuses.Contains(status);

    public static bool IsClosed(ServiceOrderStatus status) => ClosedStatuses.Contains(status);

    /// <summary>Estados a los que puede pasar una orden desde <paramref name="status"/>, en el orden habitual del trabajo.</summary>
    public static IReadOnlyList<ServiceOrderStatus> NextStatuses(ServiceOrderStatus status) =>
        Transitions.TryGetValue(status, out var next) ? next : [];

    public static bool CanTransition(ServiceOrderStatus from, ServiceOrderStatus to) => NextStatuses(from).Contains(to);

    /// <summary>Nombre del estado en español, tal como se muestra a los usuarios.</summary>
    public static string Name(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => "Recibida",
        ServiceOrderStatus.Diagnosing => "En diagnóstico",
        ServiceOrderStatus.InRepair => "En reparación",
        ServiceOrderStatus.AwaitingParts => "En espera de repuestos",
        ServiceOrderStatus.ReadyForPickup => "Lista para entrega",
        ServiceOrderStatus.Delivered => "Entregada",
        ServiceOrderStatus.Cancelled => "Cancelada",
        _ => "Desconocido",
    };
}
