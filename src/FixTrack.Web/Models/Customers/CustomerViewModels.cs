using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;

namespace FixTrack.Web.Models.Customers;

/// <summary>Formulario de registro o edición. <see cref="CustomerId"/> es null al registrar.</summary>
public sealed record CustomerEditorViewModel(int? CustomerId, string? CustomerName, CustomerFormModel Form)
{
    public bool IsNew => CustomerId is null;
}

public sealed record CustomerIndexViewModel(string? SearchText, StatusFilter Status, CustomerSearchResult Result)
{
    public bool HasSearch => !string.IsNullOrEmpty(SearchText);

    /// <summary>No existe ningún cliente registrado todavía.</summary>
    public bool IsFirstUse => !HasSearch && Result.ActiveCount + Result.InactiveCount == 0;
}

/// <summary>Detalle de un cliente con sus equipos y sus órdenes de servicio más recientes.</summary>
public sealed record CustomerDetailsViewModel(
    CustomerDetails Customer,
    IReadOnlyList<CustomerDeviceItem> Devices,
    CustomerServiceOrders Orders);
