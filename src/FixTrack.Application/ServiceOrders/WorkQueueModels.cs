using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.ServiceOrders;

/// <summary>Datos de una orden abierta necesarios para priorizarla y mostrarla en la cola de trabajo.</summary>
public sealed record OpenOrderSnapshot(
    int Id,
    ServiceOrderStatus Status,
    ServiceOrderPriority Priority,
    DateTime ReceivedAtUtc,
    DateTime? PromisedAtUtc,
    int? TechnicianId,
    string? TechnicianName,
    string CustomerName,
    DeviceType DeviceType,
    string DeviceBrand,
    string? DeviceModel)
{
    public string Number => ServiceOrder.FormatNumber(Id);
}

/// <summary>Orden con su puntuación y su posición (desde 1) dentro de su lista.</summary>
public sealed record PrioritizedOrder(OpenOrderSnapshot Order, WorkPriorityScore Score, int Position);

/// <summary>Órdenes abiertas repartidas en sus tres listas y ordenadas por el algoritmo de priorización.</summary>
/// <param name="CalculatedAtUtc">Momento del cálculo: la antigüedad y los bonos de fecha dependen de él.</param>
/// <param name="IsTruncated">Hay más órdenes abiertas que el máximo que se prioriza en una consulta.</param>
public sealed record WorkQueueResult(
    IReadOnlyList<PrioritizedOrder> Repair,
    IReadOnlyList<PrioritizedOrder> AwaitingParts,
    IReadOnlyList<PrioritizedOrder> ReadyForPickup,
    DateTime CalculatedAtUtc,
    bool IsTruncated)
{
    public int Count => Repair.Count + AwaitingParts.Count + ReadyForPickup.Count;
}
