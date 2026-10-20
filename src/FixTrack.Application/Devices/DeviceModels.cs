using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Devices;

/// <summary>Datos técnicos capturados en el formulario de equipo, antes de normalizar.</summary>
public sealed record DeviceInput(DeviceType? Type, string? Brand, string? Model, string? SerialNumber, string? Notes);

/// <summary>Registro de un equipo: incluye el cliente propietario, que después no puede cambiarse.</summary>
public sealed record RegisterDeviceInput(int? CustomerId, DeviceInput Details);

/// <summary>Criterios del listado de equipos.</summary>
public sealed record DeviceSearch(string? Text, DeviceType? Type = null, int Page = 1)
{
    public const int PageSize = 20;
    public const int TextMaxLength = 100;
}

public sealed record DeviceListItem(
    int Id,
    DeviceType Type,
    string Brand,
    string? Model,
    string? SerialNumber,
    int CustomerId,
    string CustomerName,
    bool CustomerIsActive,
    int ServiceOrderCount,
    DateTime CreatedAtUtc);

/// <summary>Equipo dentro del detalle de su cliente.</summary>
public sealed record CustomerDeviceItem(
    int Id,
    DeviceType Type,
    string Brand,
    string? Model,
    string? SerialNumber,
    int ServiceOrderCount,
    DateTime CreatedAtUtc);

/// <summary>Orden de servicio resumida, tal como se muestra en el detalle de un equipo o un técnico.</summary>
public sealed record ServiceOrderSummary(
    int Id,
    ServiceOrderStatus Status,
    ServiceOrderPriority Priority,
    DateTime ReceivedAtUtc,
    DateTime? PromisedAtUtc)
{
    public string Number => $"OS-{Id:D6}";
}

public sealed record DeviceDetails(
    int Id,
    DeviceType Type,
    string Brand,
    string? Model,
    string? SerialNumber,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    CustomerOption Owner,
    IReadOnlyList<ServiceOrderSummary> ServiceOrders);
