using FixTrack.Application.Common;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Web.Models;

/// <summary>Filtro Activos / Inactivos / Todos con la cantidad de registros de cada estado.</summary>
public sealed record StatusTabsViewModel(StatusFilter Current, int ActiveCount, int InactiveCount, Func<StatusFilter, string> Url);

/// <summary>Resumen y enlaces de paginación de un listado.</summary>
public sealed record PaginationViewModel(IPageInfo Page, Func<int, string> PageUrl);

/// <summary>Entrega comprometida de una orden con su aviso de atraso o de cercanía.</summary>
public sealed record OrderDueViewModel(ServiceOrderStatus Status, DateTime? PromisedAtUtc, DateTime? DeliveredAtUtc, DateTime NowUtc);

/// <summary>Selector de prioridad (control segmentado) con su error de validación.</summary>
public sealed record PrioritySelectorViewModel(ServiceOrderPriority? Selected, string? Error);
