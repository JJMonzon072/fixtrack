using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.ServiceOrders;

/// <summary>Datos capturados al registrar una orden, antes de normalizar.</summary>
/// <param name="CustomerId">Cliente elegido en el formulario; debe ser el propietario del equipo.</param>
/// <param name="TechnicianId">Técnico responsable desde la recepción (opcional).</param>
public sealed record ServiceOrderInput(
    int? CustomerId,
    int? DeviceId,
    string? ReportedIssue,
    ServiceOrderPriority? Priority,
    int? TechnicianId,
    DateTime? PromisedAtUtc,
    decimal? EstimatedCost,
    string? IntakeNotes);

/// <summary>Nueva planificación de una orden abierta.</summary>
public sealed record ServiceOrderPlanInput(ServiceOrderPriority? Priority, DateTime? PromisedAtUtc, decimal? EstimatedCost);

/// <summary>Cambio de estado solicitado. <see cref="FinalCost"/> solo se usa al entregar.</summary>
public sealed record ServiceOrderStatusInput(ServiceOrderStatus? Status, string? Note, decimal? FinalCost);

/// <summary>Vistas rápidas del listado de órdenes.</summary>
public enum ServiceOrderView
{
    /// <summary>Órdenes que siguen en el taller.</summary>
    Open,

    /// <summary>Órdenes abiertas cuya entrega comprometida ya pasó.</summary>
    Overdue,

    /// <summary>Órdenes entregadas o canceladas.</summary>
    Closed,

    All,
}

/// <summary>Criterios del listado de órdenes.</summary>
/// <param name="Text">Número de orden, cliente, equipo (marca, modelo o serie) o técnico.</param>
/// <param name="Status">Estado exacto dentro de la vista; se ignora si no corresponde a ella.</param>
/// <param name="TechnicianId">Técnico asignado. Se ignora si <paramref name="UnassignedOnly"/> es verdadero.</param>
/// <param name="UnassignedOnly">Solo órdenes sin técnico.</param>
/// <param name="CustomerId">Solo las órdenes de los equipos de un cliente.</param>
/// <param name="DeviceId">Solo las órdenes de un equipo.</param>
public sealed record ServiceOrderSearch(
    string? Text,
    ServiceOrderView View = ServiceOrderView.Open,
    ServiceOrderStatus? Status = null,
    ServiceOrderPriority? Priority = null,
    int? TechnicianId = null,
    bool UnassignedOnly = false,
    int Page = 1,
    int? CustomerId = null,
    int? DeviceId = null)
{
    public const int PageSize = 20;
    public const int TextMaxLength = 100;
}

public sealed record ServiceOrderListItem(
    int Id,
    ServiceOrderStatus Status,
    ServiceOrderPriority Priority,
    int CustomerId,
    string CustomerName,
    int DeviceId,
    DeviceType DeviceType,
    string DeviceBrand,
    string? DeviceModel,
    int? TechnicianId,
    string? TechnicianName,
    DateTime ReceivedAtUtc,
    DateTime? PromisedAtUtc,
    DateTime? DeliveredAtUtc)
{
    public string Number => ServiceOrder.FormatNumber(Id);

    public bool IsOverdueAt(DateTime nowUtc) => ServiceOrderSchedule.IsOverdue(Status, PromisedAtUtc, nowUtc);
}

/// <summary>Cantidad de órdenes de cada vista que coinciden con los demás filtros.</summary>
public sealed record ServiceOrderViewCounts(int Open, int Overdue, int Closed)
{
    public int All => Open + Closed;
}

public sealed record ServiceOrderSearchResult(PagedResult<ServiceOrderListItem> Page, ServiceOrderViewCounts Counts);

/// <summary>Órdenes de los equipos de un cliente: cantidades y las más recientes.</summary>
public sealed record CustomerServiceOrders(int TotalCount, int OpenCount, IReadOnlyList<ServiceOrderSummary> Recent);

public sealed record ServiceOrderDeviceInfo(int Id, DeviceType Type, string Brand, string? Model, string? SerialNumber);

public sealed record ServiceOrderTechnicianInfo(int Id, string FullName, string Specialty, bool IsActive, DateTime AssignedAtUtc);

public sealed record ServiceOrderHistoryEntry(ServiceOrderStatus? FromStatus, ServiceOrderStatus ToStatus, DateTime ChangedAtUtc, string? Note);

public sealed record ServiceOrderDetails(
    int Id,
    ServiceOrderStatus Status,
    ServiceOrderPriority Priority,
    string ReportedIssue,
    string? IntakeNotes,
    string? Diagnosis,
    decimal? EstimatedCost,
    decimal? FinalCost,
    DateTime ReceivedAtUtc,
    DateTime? PromisedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? UpdatedAtUtc,
    int Version,
    CustomerOption Customer,
    ServiceOrderDeviceInfo Device,
    ServiceOrderTechnicianInfo? Technician,
    IReadOnlyList<ServiceOrderHistoryEntry> History)
{
    public string Number => ServiceOrder.FormatNumber(Id);

    public bool IsClosed => ServiceOrderWorkflow.IsClosed(Status);

    public decimal? CostVariance => FinalCost - EstimatedCost;

    public bool IsOverdueAt(DateTime nowUtc) => ServiceOrderSchedule.IsOverdue(Status, PromisedAtUtc, nowUtc);

    /// <summary>Estados a los que puede pasar la orden, con el motivo si todavía no es posible.</summary>
    public IReadOnlyList<ServiceOrderNextStep> NextSteps =>
    [
        .. ServiceOrderWorkflow.NextStatuses(Status).Select(next => new ServiceOrderNextStep(
            next,
            ServiceOrderWorkflow.TransitionBlocker(Status, next, Technician is not null, Diagnosis is not null))),
    ];

    public bool CanRecordDiagnosis => ServiceOrderWorkflow.DiagnosisStatuses.Contains(Status);
}

/// <summary>Estado siguiente posible. <see cref="Blocker"/> explica qué falta para poder elegirlo.</summary>
public sealed record ServiceOrderNextStep(ServiceOrderStatus Status, string? Blocker)
{
    public bool IsAvailable => Blocker is null;
}
