using FixTrack.Application.Common;

namespace FixTrack.Web.Models;

/// <summary>Filtro Activos / Inactivos / Todos con la cantidad de registros de cada estado.</summary>
public sealed record StatusTabsViewModel(StatusFilter Current, int ActiveCount, int InactiveCount, Func<StatusFilter, string> Url);

/// <summary>Resumen y enlaces de paginación de un listado.</summary>
public sealed record PaginationViewModel(IPageInfo Page, Func<int, string> PageUrl);
