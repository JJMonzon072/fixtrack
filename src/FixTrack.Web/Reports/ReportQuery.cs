using System.Globalization;
using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Models;

namespace FixTrack.Web.Reports;

/// <summary>
/// Parámetros del centro de reportes (y de <c>/api/v1/metricas</c>) tal como llegan en la dirección:
/// <c>rango=7d|30d|90d|anio</c>, o <c>desde</c> y <c>hasta</c> (AAAA-MM-DD, hora de Guatemala, ambos incluidos);
/// <c>estado</c> y <c>prioridad</c> con los mismos valores que el listado de órdenes; <c>tecnico</c> con un Id o
/// <c>sin-asignar</c>. Sin período se usan los últimos 30 días.
/// </summary>
public sealed record ReportQuery(
    string? Range,
    string? From,
    string? To,
    string? Status,
    string? Priority,
    string? Technician)
{
    public const string UnassignedSlug = "sin-asignar";
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Filtros rápidos, en el orden en que se muestran.</summary>
    public static IReadOnlyList<(ReportQuickRange Range, string Slug, string Label)> QuickRanges { get; } =
    [
        (ReportQuickRange.Last7Days, "7d", "Últimos 7 días"),
        (ReportQuickRange.Last30Days, "30d", "Últimos 30 días"),
        (ReportQuickRange.Last90Days, "90d", "Últimos 90 días"),
        (ReportQuickRange.ThisYear, "anio", "Este año"),
    ];

    /// <summary>
    /// Convierte los parámetros en un filtro, o devuelve los errores con las claves de los parámetros (<c>desde</c>,
    /// <c>hasta</c>, <c>rango</c>, <c>estado</c>, <c>prioridad</c>, <c>tecnico</c>).
    /// </summary>
    /// <param name="technicianExists">Indica si existe el técnico con ese Id.</param>
    public ReportFilter? TryCreateFilter(
        DateOnly today,
        TimeZoneInfo timeZone,
        Func<int, bool> technicianExists,
        out IReadOnlyDictionary<string, string> errors)
    {
        var problems = new Dictionary<string, string>();
        var period = ResolvePeriod(today, timeZone, problems);

        ServiceOrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(Status) && (status = DomainDisplay.ParseStatus(Status)) is null)
        {
            problems["estado"] = "El estado indicado no existe.";
        }

        ServiceOrderPriority? priority = null;
        if (!string.IsNullOrWhiteSpace(Priority) && (priority = DomainDisplay.ParsePriority(Priority)) is null)
        {
            problems["prioridad"] = "La prioridad indicada no existe.";
        }

        int? technicianId = null;
        var unassigned = false;
        if (!string.IsNullOrWhiteSpace(Technician))
        {
            var value = Technician.Trim();
            if (string.Equals(value, UnassignedSlug, StringComparison.OrdinalIgnoreCase))
            {
                unassigned = true;
            }
            else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 && technicianExists(id))
            {
                technicianId = id;
            }
            else
            {
                problems["tecnico"] = "El técnico indicado no existe.";
            }
        }

        errors = problems;
        return problems.Count == 0 && period is not null
            ? new ReportFilter(period, status, priority, technicianId, unassigned)
            : null;
    }

    /// <summary>Filtro rápido elegido, si el período no se indicó con fechas.</summary>
    public ReportQuickRange? QuickRange
    {
        get
        {
            var slug = Range?.Trim().ToLowerInvariant();
            foreach (var quick in QuickRanges)
            {
                if (!HasDates && quick.Slug == slug)
                {
                    return quick.Range;
                }
            }

            return null;
        }
    }

    public static string Slug(ReportQuickRange range) => QuickRanges.First(quick => quick.Range == range).Slug;

    private bool HasDates => !string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(To);

    private ReportPeriod? ResolvePeriod(DateOnly today, TimeZoneInfo timeZone, Dictionary<string, string> problems)
    {
        if (HasDates)
        {
            var from = ParseDate(From, "desde", problems);
            var to = ParseDate(To, "hasta", problems);
            if (problems.Count > 0)
            {
                return null;
            }

            foreach (var (key, message) in ReportPeriod.Validate(from, to, today))
            {
                problems[key == nameof(ReportPeriod.From) ? "desde" : "hasta"] = message;
            }

            return problems.Count == 0 ? ReportPeriod.Create(from!.Value, to!.Value, timeZone) : null;
        }

        if (string.IsNullOrWhiteSpace(Range))
        {
            return ReportPeriod.ForQuickRange(ReportQuickRange.Last30Days, today, timeZone);
        }

        if (QuickRange is { } range)
        {
            return ReportPeriod.ForQuickRange(range, today, timeZone);
        }

        problems["rango"] = "El rango indicado no existe. Usa 7d, 30d, 90d o anio.";
        return null;
    }

    private static DateOnly? ParseDate(string? value, string key, Dictionary<string, string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems[key] = key == "desde" ? "Indica la fecha inicial." : "Indica la fecha final.";
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        problems[key] = "Escribe la fecha con el formato AAAA-MM-DD.";
        return null;
    }
}
