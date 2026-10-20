using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Web.Models;

/// <summary>Textos en español e íconos de las enumeraciones del dominio.</summary>
public static class DomainDisplay
{
    public static IReadOnlyList<DeviceType> DeviceTypes { get; } = Enum.GetValues<DeviceType>();

    public static string Name(DeviceType type) => type switch
    {
        DeviceType.Laptop => "Laptop",
        DeviceType.Desktop => "Computadora de escritorio",
        DeviceType.AllInOne => "Todo en uno",
        DeviceType.Tablet => "Tableta",
        DeviceType.Printer => "Impresora",
        DeviceType.Monitor => "Monitor",
        _ => "Otro equipo",
    };

    public static string Icon(DeviceType type) => type switch
    {
        DeviceType.Laptop => "laptop",
        DeviceType.Desktop => "pc-case",
        DeviceType.AllInOne => "monitor-speaker",
        DeviceType.Tablet => "tablet",
        DeviceType.Printer => "printer",
        DeviceType.Monitor => "monitor",
        _ => "cpu",
    };

    /// <summary>Valor del parámetro <c>?tipo=</c> del listado de equipos.</summary>
    public static string Slug(DeviceType type) => type switch
    {
        DeviceType.Laptop => "laptop",
        DeviceType.Desktop => "escritorio",
        DeviceType.AllInOne => "todo-en-uno",
        DeviceType.Tablet => "tableta",
        DeviceType.Printer => "impresora",
        DeviceType.Monitor => "monitor",
        _ => "otro",
    };

    public static DeviceType? ParseDeviceType(string? slug) =>
        DeviceTypes.Cast<DeviceType?>().FirstOrDefault(type => Slug(type!.Value) == slug?.Trim().ToLowerInvariant());

    /// <summary>Marca y modelo, por ejemplo "Dell Latitude 5440".</summary>
    public static string DeviceTitle(string brand, string? model) => model is null ? brand : $"{brand} {model}";

    public static IReadOnlyList<ServiceOrderStatus> Statuses { get; } = Enum.GetValues<ServiceOrderStatus>();

    public static IReadOnlyList<ServiceOrderPriority> Priorities { get; } = Enum.GetValues<ServiceOrderPriority>();

    public static string Name(ServiceOrderStatus status) => ServiceOrderWorkflow.Name(status);

    public static string Icon(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => "inbox",
        ServiceOrderStatus.Diagnosing => "scan-search",
        ServiceOrderStatus.InRepair => "wrench",
        ServiceOrderStatus.AwaitingParts => "package-search",
        ServiceOrderStatus.ReadyForPickup => "package-check",
        ServiceOrderStatus.Delivered => "circle-check",
        _ => "circle-x",
    };

    /// <summary>Valor del atributo <c>data-status</c>: define el color del indicador de estado en site.css.</summary>
    public static string Tone(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => "received",
        ServiceOrderStatus.Diagnosing => "diagnosing",
        ServiceOrderStatus.InRepair => "repair",
        ServiceOrderStatus.AwaitingParts => "parts",
        ServiceOrderStatus.ReadyForPickup => "ready",
        ServiceOrderStatus.Delivered => "delivered",
        _ => "cancelled",
    };

    /// <summary>Qué significa pasar a un estado, tal como se explica al cambiarlo.</summary>
    public static string Description(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => "El equipo ingresó al taller y espera revisión.",
        ServiceOrderStatus.Diagnosing => "El técnico revisa el equipo para identificar la falla.",
        ServiceOrderStatus.InRepair => "La reparación está en curso.",
        ServiceOrderStatus.AwaitingParts => "El trabajo se detiene hasta recibir un repuesto. Indica cuál en la nota.",
        ServiceOrderStatus.ReadyForPickup => "El trabajo terminó y el equipo espera que el cliente lo retire.",
        ServiceOrderStatus.Delivered => "El cliente retiró el equipo. Se registra el costo final y la orden se cierra.",
        _ => "La orden se cierra sin completar el servicio. Indica el motivo en la nota.",
    };

    /// <summary>Valor del parámetro <c>?estado=</c> del listado de órdenes.</summary>
    public static string Slug(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.Received => "recibida",
        ServiceOrderStatus.Diagnosing => "en-diagnostico",
        ServiceOrderStatus.InRepair => "en-reparacion",
        ServiceOrderStatus.AwaitingParts => "en-espera-de-repuestos",
        ServiceOrderStatus.ReadyForPickup => "lista-para-entrega",
        ServiceOrderStatus.Delivered => "entregada",
        _ => "cancelada",
    };

    public static ServiceOrderStatus? ParseStatus(string? slug) =>
        Statuses.Cast<ServiceOrderStatus?>().FirstOrDefault(status => Slug(status!.Value) == slug?.Trim().ToLowerInvariant());

    public static string BadgeClass(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.ReadyForPickup or ServiceOrderStatus.Delivered => "badge--accent",
        _ => "badge--neutral",
    };

    public static string Name(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => "Baja",
        ServiceOrderPriority.Normal => "Normal",
        ServiceOrderPriority.High => "Alta",
        _ => "Urgente",
    };

    public static string Icon(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => "arrow-down",
        ServiceOrderPriority.Normal => "minus",
        ServiceOrderPriority.High => "arrow-up",
        _ => "triangle-alert",
    };

    /// <summary>Valor del atributo <c>data-priority</c> y del parámetro <c>?prioridad=</c>.</summary>
    public static string Slug(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => "baja",
        ServiceOrderPriority.Normal => "normal",
        ServiceOrderPriority.High => "alta",
        _ => "urgente",
    };

    public static ServiceOrderPriority? ParsePriority(string? slug) =>
        Priorities.Cast<ServiceOrderPriority?>().FirstOrDefault(priority => Slug(priority!.Value) == slug?.Trim().ToLowerInvariant());

    public static string Name(RecommendedAction action) => action switch
    {
        RecommendedAction.AssignTechnician => "Asignar técnico",
        RecommendedAction.StartDiagnosis => "Iniciar diagnóstico",
        RecommendedAction.CompleteDiagnosis => "Completar diagnóstico",
        RecommendedAction.ContinueRepair => "Continuar reparación",
        RecommendedAction.FollowUpParts => "Dar seguimiento al repuesto",
        _ => "Avisar al cliente y entregar",
    };

    public static string Icon(RecommendedAction action) => action switch
    {
        RecommendedAction.AssignTechnician => "user-check",
        RecommendedAction.StartDiagnosis or RecommendedAction.CompleteDiagnosis => "scan-search",
        RecommendedAction.ContinueRepair => "wrench",
        RecommendedAction.FollowUpParts => "package-search",
        _ => "package-check",
    };

    public static string Name(WorkQueue queue) => queue switch
    {
        WorkQueue.Repair => "Reparaciones",
        WorkQueue.AwaitingParts => "En espera de repuestos",
        _ => "Listas para entrega",
    };

    /// <summary>Valor del parámetro <c>?vista=</c> del listado de órdenes.</summary>
    public static string Slug(ServiceOrderView view) => view switch
    {
        ServiceOrderView.Overdue => "vencidas",
        ServiceOrderView.Closed => "cerradas",
        ServiceOrderView.All => "todas",
        _ => "abiertas",
    };

    public static ServiceOrderView ParseView(string? slug) => slug?.Trim().ToLowerInvariant() switch
    {
        "vencidas" => ServiceOrderView.Overdue,
        "cerradas" => ServiceOrderView.Closed,
        "todas" => ServiceOrderView.All,
        _ => ServiceOrderView.Open,
    };
}
