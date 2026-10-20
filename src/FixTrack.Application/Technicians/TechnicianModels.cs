using FixTrack.Application.Common;
using FixTrack.Application.Devices;

namespace FixTrack.Application.Technicians;

/// <summary>Datos capturados en el formulario de técnico, antes de normalizar.</summary>
public sealed record TechnicianInput(string? FullName, string? Specialty, string? Phone, string? Email);

/// <summary>Criterios del listado de técnicos.</summary>
public sealed record TechnicianSearch(string? Text, StatusFilter Status = StatusFilter.Active, int Page = 1)
{
    public const int PageSize = 20;
    public const int TextMaxLength = 100;
}

public sealed record TechnicianListItem(
    int Id,
    string FullName,
    string Specialty,
    string? Phone,
    string? Email,
    bool IsActive,
    int OpenOrderCount,
    DateTime CreatedAtUtc);

/// <summary>Una página del listado y la cantidad de técnicos por estado que coinciden con la búsqueda.</summary>
public sealed record TechnicianSearchResult(PagedResult<TechnicianListItem> Page, int ActiveCount, int InactiveCount);

public sealed record TechnicianDetails(
    int Id,
    string FullName,
    string Specialty,
    string? Phone,
    string? Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? DeactivatedAtUtc,
    int OpenOrderCount,
    int TotalOrderCount,
    IReadOnlyList<ServiceOrderSummary> OpenOrders);

/// <summary>Técnico con su carga de trabajo: órdenes abiertas asignadas.</summary>
public sealed record TechnicianWorkload(int Id, string FullName, string Specialty, bool IsActive, int OpenOrderCount);
