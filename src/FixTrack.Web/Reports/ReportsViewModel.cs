using System.Globalization;
using FixTrack.Application.Reports;
using FixTrack.Application.Technicians;
using FixTrack.Web.Models;

namespace FixTrack.Web.Reports;

/// <summary>Datos de la página del centro de reportes.</summary>
/// <param name="Report">El reporte calculado; <c>null</c> si los filtros no son válidos.</param>
/// <param name="Errors">Errores de los filtros, con las claves de los parámetros.</param>
/// <param name="CanExport">La cuenta puede descargar CSV (Administrador u Operador).</param>
public sealed record ReportsViewModel(
    ReportQuery Query,
    OperationsReport? Report,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<TechnicianWorkload> Technicians,
    DateOnly Today,
    bool CanExport,
    int ExportMaxRows)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    /// <summary>Período mostrado en el formulario (el calculado o, si hay errores, lo que se escribió).</summary>
    public string FromValue => Report?.Period.From.ToString(ReportQuery.DateFormat, CultureInfo.InvariantCulture) ?? Query.From ?? string.Empty;

    public string ToValue => Report?.Period.To.ToString(ReportQuery.DateFormat, CultureInfo.InvariantCulture) ?? Query.To ?? string.Empty;

    /// <summary>Filtro rápido que coincide con el período mostrado.</summary>
    public ReportQuickRange? ActiveQuickRange =>
        Report is null ? null : ReportQuery.QuickRanges.Select(quick => (ReportQuickRange?)quick.Range).FirstOrDefault(range => Report.Period.Matches(range!.Value, Today));

    /// <summary>Parámetros de la dirección con el período actual y los filtros (para enlaces y la exportación).</summary>
    public Dictionary<string, string?> RouteValues(ReportQuickRange? quickRange = null) => new()
    {
        ["rango"] = quickRange is { } range ? ReportQuery.Slug(range) : null,
        ["desde"] = quickRange is null ? FromValue : null,
        ["hasta"] = quickRange is null ? ToValue : null,
        ["estado"] = Report?.Filter.Status is { } status ? DomainDisplay.Slug(status) : Query.Status,
        ["prioridad"] = Report?.Filter.Priority is { } priority ? DomainDisplay.Slug(priority) : Query.Priority,
        ["tecnico"] = Report?.Filter.UnassignedOnly == true ? ReportQuery.UnassignedSlug : Report?.Filter.TechnicianId?.ToString(CultureInfo.InvariantCulture) ?? Query.Technician,
    };

    public bool HasRecordFilters => Report?.Filter is { } filter && (filter.Status is not null || filter.Priority is not null || filter.TechnicianId is not null || filter.UnassignedOnly);

    /// <summary>"1 oct 2026 – 31 oct 2026".</summary>
    public static string PeriodLabel(ReportPeriod period) =>
        period.From == period.To ? ShortDate(period.From) : $"{ShortDate(period.From)} – {ShortDate(period.To)}";

    public static string ShortDate(DateOnly date) => date.ToString("d MMM yyyy", Spanish).Replace(".", string.Empty, StringComparison.Ordinal);

    public static string GranularityLabel(ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Day => "por día",
        ReportGranularity.Week => "por semana",
        _ => "por mes",
    };

    /// <summary>Duración promedio legible: "18.5 h" o "4.6 días"; "Sin datos" si no se pudo calcular.</summary>
    public static string Hours(double? hours) => hours switch
    {
        null => "Sin datos",
        < 48 => $"{hours.Value.ToString("0.#", CultureInfo.InvariantCulture)} h",
        _ => $"{(hours.Value / 24).ToString("0.#", CultureInfo.InvariantCulture)} días",
    };

    public static string Percent(int? percent) => percent is { } value ? $"{value} %" : "Sin datos";

    public static string Amount(decimal? amount) => amount is { } value ? Money.Format(value) : "Sin datos";

    /// <summary>Variación porcentual con signo, por ejemplo "+1.7 %"; el monto si no hay porcentaje; "Sin datos".</summary>
    public static string VariancePercent(decimal? variance, decimal? percent) => (variance, percent) switch
    {
        (null, _) => "Sin datos",
        (_, { } share) => $"{Sign(share)}{Math.Abs(share).ToString("0.0", CultureInfo.InvariantCulture)} %",
        ({ } amount, null) => VarianceAmount(amount),
    };

    /// <summary>Monto de la variación con signo, por ejemplo "+Q 20.00".</summary>
    public static string VarianceAmount(decimal variance) => $"{Sign(variance)}Q {Math.Abs(variance).ToString("N2", CultureInfo.InvariantCulture)}";

    private static string Sign(decimal value) => value > 0 ? "+" : value < 0 ? "−" : string.Empty;
}

/// <summary>Un gráfico de la serie del período, con su texto alternativo.</summary>
public sealed record ReportChartModel(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<ReportSeriesPoint> Series,
    ReportGranularity Granularity);

/// <summary>Conclusiones breves calculadas a partir del reporte, para decidir sin leer cada tabla.</summary>
public static class ReportInsights
{
    public static IReadOnlyList<(string Icon, string Text)> For(OperationsReport report)
    {
        var insights = new List<(string Icon, string Text)>();
        if (report.DeliveredWithPromise > 0)
        {
            var onTime = report.DeliveredWithPromise - report.DeliveredLate;
            insights.Add(("clock", report.DeliveredLate == 0
                ? $"Todas las entregas con fecha comprometida ({report.DeliveredWithPromise}) se cumplieron a tiempo."
                : $"{onTime} de {report.DeliveredWithPromise} entregas con fecha comprometida llegaron a tiempo ({report.OnTimePercent} %); {ReportCharts.Plural(report.DeliveredLate, "se entregó", "se entregaron")} con atraso."));
        }
        else if (report.Delivered > 0)
        {
            insights.Add(("clock", "Ninguna entrega del período tenía fecha comprometida: no se puede medir la puntualidad."));
        }

        if (report.AverageHoursToDelivery is { } delivery && report.AverageHoursToReady is { } ready && report.ReadyMeasured == report.Delivered && delivery >= ready)
        {
            insights.Add(("timer", $"Las órdenes quedan listas en {ReportsViewModel.Hours(ready)} en promedio y el cliente las retira {ReportsViewModel.Hours(delivery - ready)} después."));
        }

        if (report.CostVariance is { } variance && report.CostVariancePercent is { } percent)
        {
            insights.Add(("banknote", variance switch
            {
                > 0 => $"Los costos finales superaron lo estimado en {percent.ToString("0.0", CultureInfo.InvariantCulture)} % ({report.DeliveredWithEstimate} entregas con estimación).",
                < 0 => $"Los costos finales quedaron {Math.Abs(percent).ToString("0.0", CultureInfo.InvariantCulture)} % por debajo de lo estimado ({report.DeliveredWithEstimate} entregas con estimación).",
                _ => $"Los costos finales coincidieron con lo estimado ({report.DeliveredWithEstimate} entregas con estimación).",
            }));
        }

        if (report.Technicians.FirstOrDefault(row => row.TechnicianId is null) is { OpenNow: > 0 } unassigned)
        {
            insights.Add(("triangle-alert", $"{ReportCharts.Plural(unassigned.OpenNow, "orden abierta no tiene", "órdenes abiertas no tienen")} técnico asignado en este momento."));
        }

        return insights;
    }

    /// <summary>Texto alternativo del gráfico de volumen.</summary>
    public static string VolumeDescription(OperationsReport report)
    {
        var granularity = report.Period.Granularity;
        var text = $"{ReportsViewModel.PeriodLabel(report.Period)}, {ReportsViewModel.GranularityLabel(granularity)}: " +
            $"{ReportCharts.Plural(report.Series.Sum(point => point.Received), "orden recibida", "órdenes recibidas")} y " +
            $"{ReportCharts.Plural(report.Series.Sum(point => point.Delivered), "entregada", "entregadas")}.";
        if (report.Series.MaxBy(point => point.Received) is { Received: > 0 } busiest)
        {
            text += $" Más recepciones: {ReportCharts.LongLabel(busiest, granularity)} ({busiest.Received}).";
        }

        return text;
    }

    /// <summary>Texto alternativo del gráfico de ingresos registrados.</summary>
    public static string RevenueDescription(OperationsReport report)
    {
        var granularity = report.Period.Granularity;
        var text = $"{ReportsViewModel.PeriodLabel(report.Period)}, {ReportsViewModel.GranularityLabel(granularity)}: " +
            $"{Money.Format(report.Revenue)} registrados al entregar.";
        if (report.Series.MaxBy(point => point.Revenue) is { Revenue: > 0 } best)
        {
            text += $" Mayor monto: {ReportCharts.LongLabel(best, granularity)} ({Money.Format(best.Revenue)}).";
        }

        return text;
    }
}
