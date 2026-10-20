using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Web.Models.ServiceOrders;

/// <summary>Filtros del listado tal como aparecen en la dirección (<c>?buscar=&amp;vista=&amp;estado=&amp;prioridad=&amp;tecnico=</c>).</summary>
public sealed record ServiceOrderListQuery(
    string? Text,
    ServiceOrderView View,
    ServiceOrderStatus? Status,
    ServiceOrderPriority? Priority,
    int? TechnicianId,
    bool Unassigned,
    int? CustomerId = null,
    int? DeviceId = null)
{
    public const string UnassignedValue = "sin-asignar";

    public string? TechnicianValue => Unassigned ? UnassignedValue : TechnicianId?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool HasFilters =>
        !string.IsNullOrEmpty(Text) || Status is not null || Priority is not null || TechnicianValue is not null || HasRecordFilter;

    /// <summary>El listado está limitado a un cliente o a un equipo (enlaces "Ver todas" de sus detalles).</summary>
    public bool HasRecordFilter => CustomerId is not null || DeviceId is not null;

    /// <summary>Lee los parámetros de la dirección. Un estado que no corresponde a la vista se descarta.</summary>
    public static ServiceOrderListQuery Parse(
        string? text,
        string? view,
        string? status,
        string? priority,
        string? technician,
        int? customer = null,
        int? device = null)
    {
        var parsedView = DomainDisplay.ParseView(view);
        var parsedStatus = DomainDisplay.ParseStatus(status);
        if (parsedStatus is { } value && !StatusesFor(parsedView).Contains(value))
        {
            parsedStatus = null;
        }

        var unassigned = technician?.Trim().ToLowerInvariant() == UnassignedValue;
        int? technicianId = !unassigned && int.TryParse(technician, out var id) && id > 0 ? id : null;
        return new ServiceOrderListQuery(
            text?.Trim(),
            parsedView,
            parsedStatus,
            DomainDisplay.ParsePriority(priority),
            technicianId,
            unassigned,
            customer is > 0 ? customer : null,
            device is > 0 ? device : null);
    }

    /// <summary>Estados que pueden elegirse dentro de una vista.</summary>
    public static IReadOnlyList<ServiceOrderStatus> StatusesFor(ServiceOrderView view) => view switch
    {
        ServiceOrderView.Open or ServiceOrderView.Overdue => ServiceOrderWorkflow.OpenStatuses,
        ServiceOrderView.Closed => ServiceOrderWorkflow.ClosedStatuses,
        _ => DomainDisplay.Statuses,
    };

    public ServiceOrderSearch ToSearch(int page) =>
        new(Text, View, Status, Priority, TechnicianId, Unassigned, page, CustomerId, DeviceId);

    /// <summary>Valores de ruta para un enlace del listado; la vista predeterminada y la página 1 no se escriben.</summary>
    public object RouteValues(ServiceOrderView? view = null, int page = 1, bool keepRecordFilter = true)
    {
        var targetView = view ?? View;
        var keepStatus = Status is { } status && StatusesFor(targetView).Contains(status);
        return new
        {
            buscar = string.IsNullOrEmpty(Text) ? null : Text,
            vista = targetView == ServiceOrderView.Open ? null : DomainDisplay.Slug(targetView),
            estado = keepStatus ? DomainDisplay.Slug(Status!.Value) : null,
            prioridad = Priority is { } priority ? DomainDisplay.Slug(priority) : null,
            tecnico = TechnicianValue,
            cliente = keepRecordFilter ? CustomerId : null,
            equipo = keepRecordFilter ? DeviceId : null,
            pagina = page > 1 ? page : (int?)null,
        };
    }
}

public sealed record ServiceOrderIndexViewModel(
    ServiceOrderListQuery Query,
    ServiceOrderSearchResult Result,
    IReadOnlyList<TechnicianWorkload> Technicians,
    DateTime NowUtc)
{
    /// <summary>Todavía no existe ninguna orden.</summary>
    public bool IsFirstUse => !Query.HasFilters && Result.Counts.All == 0;
}

/// <summary>
/// Campos del formulario de registro. Las fechas y los montos se reciben como texto y se interpretan en el
/// controlador (hora local del taller y quetzales con punto decimal), sin depender de la cultura de la solicitud.
/// </summary>
public sealed class ServiceOrderFormModel
{
    public int? CustomerId { get; set; }

    public int? DeviceId { get; set; }

    /// <summary>El equipo llegó preseleccionado desde su detalle: el formulario no permite cambiarlo.</summary>
    public bool DeviceLocked { get; set; }

    public string? ReportedIssue { get; set; }

    public string? IntakeNotes { get; set; }

    public ServiceOrderPriority? Priority { get; set; }

    public string? PromisedAt { get; set; }

    public string? EstimatedCost { get; set; }

    public int? TechnicianId { get; set; }

    /// <summary>Identificador de este envío del formulario: si se envía dos veces, se registra una sola orden.</summary>
    public Guid? RequestId { get; set; }
}

/// <param name="Customer">Cliente elegido o preseleccionado.</param>
/// <param name="Devices">Equipos del cliente elegido.</param>
/// <param name="Technicians">Técnicos activos con su carga de trabajo.</param>
/// <param name="Notice">Aviso cuando el cliente o el equipo indicados en la dirección no admiten órdenes nuevas.</param>
public sealed record ServiceOrderEditorViewModel(
    ServiceOrderFormModel Form,
    CustomerOption? Customer,
    IReadOnlyList<CustomerDeviceItem> Devices,
    IReadOnlyList<TechnicianWorkload> Technicians,
    string? Notice);

public sealed record ServiceOrderDetailsViewModel(ServiceOrderDetails Order, DateTime NowUtc)
{
    public IReadOnlyList<TimelineEntry> Timeline { get; } = OrderTimeline.Build(Order);
}

public sealed class ServiceOrderStatusFormModel
{
    public ServiceOrderStatus? Status { get; set; }

    public string? Note { get; set; }

    public string? FinalCost { get; set; }

    public int Version { get; set; }
}

public sealed record ServiceOrderStatusViewModel(ServiceOrderDetails Order, ServiceOrderStatusFormModel Form);

public sealed record ServiceOrderAssignViewModel(
    ServiceOrderDetails Order,
    IReadOnlyList<TechnicianWorkload> Technicians,
    int? SelectedTechnicianId,
    int Version,
    string? Note = null);

public sealed record ServiceOrderDiagnosisViewModel(ServiceOrderDetails Order, string? Diagnosis, int Version);

public sealed class ServiceOrderPlanFormModel
{
    public ServiceOrderPriority? Priority { get; set; }

    public string? PromisedAt { get; set; }

    public string? EstimatedCost { get; set; }

    public int Version { get; set; }
}

public sealed record ServiceOrderPlanViewModel(ServiceOrderDetails Order, ServiceOrderPlanFormModel Form);
