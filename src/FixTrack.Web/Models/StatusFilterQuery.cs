using FixTrack.Application.Common;

namespace FixTrack.Web.Models;

/// <summary>Valor del parámetro <c>?estado=</c> de los listados (clientes y técnicos).</summary>
public static class StatusFilterQuery
{
    public static string ToParameter(StatusFilter status) => status switch
    {
        StatusFilter.Inactive => "inactivos",
        StatusFilter.All => "todos",
        _ => "activos",
    };

    public static StatusFilter Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "inactivos" => StatusFilter.Inactive,
        "todos" => StatusFilter.All,
        _ => StatusFilter.Active,
    };
}
