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

    public static string Name(ServiceOrderStatus status) => ServiceOrderWorkflow.Name(status);

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
}
