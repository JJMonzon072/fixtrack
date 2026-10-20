using FixTrack.Application.Common;

namespace FixTrack.Application.Customers;

/// <summary>Datos capturados en el formulario de cliente, antes de normalizar.</summary>
public sealed record CustomerInput(string? FullName, string? Phone, string? Email, string? Notes);

/// <summary>Criterios del listado de clientes.</summary>
public sealed record CustomerSearch(string? Text, StatusFilter Status = StatusFilter.Active, int Page = 1)
{
    public const int PageSize = 20;
    public const int TextMaxLength = 100;
}

public sealed record CustomerListItem(
    int Id,
    string FullName,
    string Phone,
    string? Email,
    bool IsActive,
    int DeviceCount,
    DateTime CreatedAtUtc);

/// <summary>Una página del listado y la cantidad de clientes por estado que coinciden con la búsqueda.</summary>
public sealed record CustomerSearchResult(
    PagedResult<CustomerListItem> Page,
    int ActiveCount,
    int InactiveCount);

public sealed record CustomerDetails(
    int Id,
    string FullName,
    string Phone,
    string? Email,
    string? Notes,
    bool IsActive,
    int DeviceCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? DeactivatedAtUtc);
