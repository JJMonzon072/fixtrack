using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Dashboard;

/// <summary>
/// Cantidades de órdenes calculadas por la base de datos en una sola consulta de agregación.
/// Las cantidades por prioridad cuentan solo órdenes abiertas.
/// </summary>
public sealed record OrderCounts(
    int Received,
    int Diagnosing,
    int InRepair,
    int AwaitingParts,
    int ReadyForPickup,
    int Delivered,
    int Cancelled,
    int Overdue,
    int Unassigned,
    int ReceivedRecently,
    int OpenLow,
    int OpenNormal,
    int OpenHigh,
    int OpenUrgent)
{
    public static OrderCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public int Open => Received + Diagnosing + InRepair + AwaitingParts + ReadyForPickup;

    public int Total => Open + Delivered + Cancelled;

    /// <summary>Órdenes abiertas de prioridad alta o urgente.</summary>
    public int HighPriorityOpen => OpenHigh + OpenUrgent;

    public int ForStatus(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => Received,
        ServiceOrderStatus.Diagnosing => Diagnosing,
        ServiceOrderStatus.InRepair => InRepair,
        ServiceOrderStatus.AwaitingParts => AwaitingParts,
        ServiceOrderStatus.ReadyForPickup => ReadyForPickup,
        ServiceOrderStatus.Delivered => Delivered,
        _ => Cancelled,
    };

    public int OpenWithPriority(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => OpenLow,
        ServiceOrderPriority.Normal => OpenNormal,
        ServiceOrderPriority.High => OpenHigh,
        _ => OpenUrgent,
    };
}

/// <summary>Información del panel general, toda calculada a partir de los datos registrados.</summary>
/// <param name="RecentDays">Días que se consideran para las órdenes recibidas recientemente.</param>
/// <param name="RecommendedActions">Las órdenes abiertas mejor puntuadas, con la acción que se recomienda para cada una.</param>
public sealed record DashboardSummary(
    OrderCounts Counts,
    int RecentDays,
    IReadOnlyList<PrioritizedOrder> RecommendedActions,
    IReadOnlyList<ServiceOrderSummary> UpcomingDeliveries,
    IReadOnlyList<ServiceOrderSummary> RecentlyReceived,
    IReadOnlyList<TechnicianWorkload> Workload,
    DateTime CalculatedAtUtc);
