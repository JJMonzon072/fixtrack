using System.Text.Json.Serialization;
using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Models;

namespace FixTrack.Web.Api;

/// <summary>
/// Métricas de un período (<c>GET /api/v1/metricas</c>): los mismos cálculos del centro de reportes, en JSON. Los promedios
/// sin datos son <c>null</c>.
/// </summary>
public sealed record ApiMetrics(
    [property: JsonPropertyName("periodo")] ApiMetricsPeriod Period,
    [property: JsonPropertyName("filtros")] ApiMetricsFilters Filters,
    [property: JsonPropertyName("ordenes")] ApiMetricsOrders Orders,
    [property: JsonPropertyName("puntualidad")] ApiMetricsPunctuality Punctuality,
    [property: JsonPropertyName("tiempos")] ApiMetricsTimes Times,
    [property: JsonPropertyName("montos")] ApiMetricsAmounts Amounts,
    [property: JsonPropertyName("porEstado")] IReadOnlyList<ApiMetricsStatus> ByStatus,
    [property: JsonPropertyName("porPrioridad")] IReadOnlyList<ApiMetricsPriority> ByPriority,
    [property: JsonPropertyName("serie")] IReadOnlyList<ApiMetricsPoint> Series,
    [property: JsonPropertyName("tecnicos")] IReadOnlyList<ApiMetricsTechnician> Technicians,
    [property: JsonPropertyName("calculadoEn")] DateTime CalculatedAtUtc)
{
    public static ApiMetrics From(OperationsReport report)
    {
        var period = report.Period;
        return new ApiMetrics(
            new ApiMetricsPeriod(period.From, period.To, period.TimeZone.Id, period.StartUtc, period.EndUtc, Granularity(period.Granularity)),
            new ApiMetricsFilters(
                report.Filter.Status is { } status ? DomainDisplay.Slug(status) : null,
                report.Filter.Priority is { } priority ? DomainDisplay.Slug(priority) : null,
                report.Filter.UnassignedOnly ? null : report.Filter.TechnicianId,
                report.Filter.UnassignedOnly),
            new ApiMetricsOrders(report.Received, report.Delivered, report.Cancelled),
            new ApiMetricsPunctuality(report.DeliveredWithPromise, report.DeliveredLate, report.OnTimePercent),
            new ApiMetricsTimes(Round(report.AverageHoursToDelivery), Round(report.AverageHoursToReady)),
            new ApiMetricsAmounts(
                "GTQ",
                report.Revenue,
                report.AverageFinalCost,
                report.DeliveredWithEstimate,
                report.EstimatedCostSum,
                report.FinalCostSumWithEstimate,
                report.CostVariance,
                report.CostVariancePercent,
                "Ingresos registrados: costo final de las órdenes entregadas en el período. FixTrack no registra pagos: no son cobros confirmados."),
            [.. report.ReceivedByStatus.Select(share => new ApiMetricsStatus(DomainDisplay.Slug(share.Key), DomainDisplay.Name(share.Key), share.Count, share.Percent))],
            [
                .. report.Priorities.Select(row => new ApiMetricsPriority(
                    DomainDisplay.Slug(row.Priority),
                    DomainDisplay.Name(row.Priority),
                    row.Received,
                    row.Delivered,
                    row.Late,
                    row.OnTimePercent,
                    Round(row.AverageHoursToDelivery))),
            ],
            [.. report.Series.Select(point => new ApiMetricsPoint(point.Start, point.End, point.Received, point.Delivered, point.Revenue))],
            [
                .. report.Technicians.Select(row => new ApiMetricsTechnician(
                    row.TechnicianId,
                    row.TechnicianId is null ? null : row.Name,
                    row.TechnicianId is null ? null : row.IsActive,
                    row.Received,
                    row.Delivered,
                    row.Late,
                    row.OnTimePercent,
                    Round(row.AverageHoursToDelivery),
                    row.FinalCostSum,
                    row.OpenNow)),
            ],
            report.CalculatedAtUtc);
    }

    private static double? Round(double? hours) => hours is { } value ? Math.Round(value, 1, MidpointRounding.AwayFromZero) : null;

    private static string Granularity(ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Day => "dia",
        ReportGranularity.Week => "semana",
        _ => "mes",
    };
}

/// <param name="From">Primer día local (incluido).</param>
/// <param name="To">Último día local (incluido).</param>
/// <param name="StartUtc">Inicio del intervalo consultado (incluido).</param>
/// <param name="EndUtc">Fin del intervalo consultado (excluido).</param>
public sealed record ApiMetricsPeriod(
    [property: JsonPropertyName("desde")] DateOnly From,
    [property: JsonPropertyName("hasta")] DateOnly To,
    [property: JsonPropertyName("zonaHoraria")] string TimeZone,
    [property: JsonPropertyName("inicioUtc")] DateTime StartUtc,
    [property: JsonPropertyName("finUtc")] DateTime EndUtc,
    [property: JsonPropertyName("agrupacion")] string Granularity);

public sealed record ApiMetricsFilters(
    [property: JsonPropertyName("estado")] string? Status,
    [property: JsonPropertyName("prioridad")] string? Priority,
    [property: JsonPropertyName("tecnico")] int? TechnicianId,
    [property: JsonPropertyName("sinTecnico")] bool UnassignedOnly);

public sealed record ApiMetricsOrders(
    [property: JsonPropertyName("recibidas")] int Received,
    [property: JsonPropertyName("entregadas")] int Delivered,
    [property: JsonPropertyName("canceladas")] int Cancelled);

public sealed record ApiMetricsPunctuality(
    [property: JsonPropertyName("entregasConFechaComprometida")] int WithPromise,
    [property: JsonPropertyName("entregasConAtraso")] int Late,
    [property: JsonPropertyName("porcentajeATiempo")] int? OnTimePercent);

public sealed record ApiMetricsTimes(
    [property: JsonPropertyName("promedioHorasHastaEntrega")] double? AverageHoursToDelivery,
    [property: JsonPropertyName("promedioHorasHastaLista")] double? AverageHoursToReady);

public sealed record ApiMetricsAmounts(
    [property: JsonPropertyName("moneda")] string Currency,
    [property: JsonPropertyName("ingresosRegistrados")] decimal Revenue,
    [property: JsonPropertyName("costoPromedioPorEntrega")] decimal? AverageFinalCost,
    [property: JsonPropertyName("entregasConCostoEstimado")] int DeliveredWithEstimate,
    [property: JsonPropertyName("costoEstimadoTotal")] decimal EstimatedCostSum,
    [property: JsonPropertyName("costoFinalDeEntregasConEstimado")] decimal FinalCostSumWithEstimate,
    [property: JsonPropertyName("diferenciaFinalMenosEstimado")] decimal? Variance,
    [property: JsonPropertyName("diferenciaPorcentual")] decimal? VariancePercent,
    [property: JsonPropertyName("nota")] string Note);

public sealed record ApiMetricsStatus(
    [property: JsonPropertyName("codigo")] string Code,
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("cantidad")] int Count,
    [property: JsonPropertyName("porcentaje")] int Percent);

public sealed record ApiMetricsPriority(
    [property: JsonPropertyName("codigo")] string Code,
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("recibidas")] int Received,
    [property: JsonPropertyName("entregadas")] int Delivered,
    [property: JsonPropertyName("entregasConAtraso")] int Late,
    [property: JsonPropertyName("porcentajeATiempo")] int? OnTimePercent,
    [property: JsonPropertyName("promedioHorasHastaEntrega")] double? AverageHoursToDelivery);

public sealed record ApiMetricsPoint(
    [property: JsonPropertyName("desde")] DateOnly Start,
    [property: JsonPropertyName("hasta")] DateOnly End,
    [property: JsonPropertyName("recibidas")] int Received,
    [property: JsonPropertyName("entregadas")] int Delivered,
    [property: JsonPropertyName("ingresosRegistrados")] decimal Revenue);

/// <summary>Actividad de un técnico; con <c>id</c> nulo, la de las órdenes sin técnico asignado.</summary>
public sealed record ApiMetricsTechnician(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("nombre")] string? Name,
    [property: JsonPropertyName("activo")] bool? IsActive,
    [property: JsonPropertyName("recibidas")] int Received,
    [property: JsonPropertyName("entregadas")] int Delivered,
    [property: JsonPropertyName("entregasConAtraso")] int Late,
    [property: JsonPropertyName("porcentajeATiempo")] int? OnTimePercent,
    [property: JsonPropertyName("promedioHorasHastaEntrega")] double? AverageHoursToDelivery,
    [property: JsonPropertyName("ingresosRegistrados")] decimal Revenue,
    [property: JsonPropertyName("abiertasAhora")] int OpenNow);
