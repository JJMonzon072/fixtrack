using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Reports;

/// <summary>
/// Criterios de un reporte. Los filtros de estado, prioridad y técnico delimitan las órdenes consideradas; el período
/// selecciona eventos, y cada indicador usa la fecha de su propio evento (recepción, entrega o cancelación).
/// </summary>
/// <param name="Status">Estado actual de la orden.</param>
/// <param name="TechnicianId">Técnico asignado actualmente. Se ignora si <paramref name="UnassignedOnly"/> es verdadero.</param>
/// <param name="UnassignedOnly">Solo órdenes sin técnico asignado.</param>
public sealed record ReportFilter(
    ReportPeriod Period,
    ServiceOrderStatus? Status = null,
    ServiceOrderPriority? Priority = null,
    int? TechnicianId = null,
    bool UnassignedOnly = false);

/// <summary>Cantidad de órdenes recibidas en el período con un estado actual y una prioridad.</summary>
public sealed record ReceivedGroup(ServiceOrderStatus Status, ServiceOrderPriority Priority, int Count);

/// <summary>
/// Órdenes entregadas en el período (por fecha de entrega) con una prioridad. Las sumas llegan calculadas por la base:
/// los promedios se obtienen después dividiendo, para no promediar promedios.
/// </summary>
/// <param name="WithPromise">Entregadas que tenían fecha comprometida.</param>
/// <param name="Late">Entregadas después de su fecha comprometida.</param>
/// <param name="FinalCostSum">Suma de los costos finales (montos registrados al entregar).</param>
/// <param name="WithEstimate">Entregadas que tenían costo estimado.</param>
/// <param name="EstimatedCostSum">Suma de los costos estimados de las entregadas con estimación.</param>
/// <param name="FinalCostSumWithEstimate">Suma de los costos finales de esas mismas órdenes.</param>
/// <param name="SecondsToDeliverySum">Suma de los segundos entre la recepción y la entrega.</param>
public sealed record DeliveredGroup(
    ServiceOrderPriority Priority,
    int Count,
    int WithPromise,
    int Late,
    decimal FinalCostSum,
    int WithEstimate,
    decimal EstimatedCostSum,
    decimal FinalCostSumWithEstimate,
    long SecondsToDeliverySum);

/// <summary>Tiempo hasta quedar lista para entrega de las órdenes entregadas en el período.</summary>
/// <param name="Count">Entregadas con al menos un paso a "Lista para entrega" en su historial.</param>
/// <param name="SecondsSum">Suma de los segundos entre la recepción y el último paso a "Lista para entrega".</param>
public sealed record ReadyTimeTotals(int Count, long SecondsSum)
{
    public static ReadyTimeTotals Empty { get; } = new(0, 0);
}

/// <summary>Órdenes de un día local: recibidas ese día, o entregadas ese día con la suma de sus costos finales.</summary>
public sealed record DailyTotal(DateOnly Day, int Count, decimal Amount);

/// <summary>Actividad de un técnico (o de las órdenes sin técnico, con <see cref="TechnicianId"/> nulo).</summary>
/// <param name="Received">Órdenes recibidas en el período que hoy tiene asignadas.</param>
/// <param name="Delivered">Órdenes entregadas en el período con este técnico.</param>
/// <param name="Late">De esas, entregadas después de la fecha comprometida.</param>
/// <param name="FinalCostSum">Suma de los costos finales de sus entregas del período.</param>
/// <param name="SecondsToDeliverySum">Suma de los segundos entre recepción y entrega de esas entregas.</param>
/// <param name="OpenNow">Órdenes abiertas que tiene asignadas en este momento (no depende del período).</param>
public sealed record TechnicianActivityRow(
    int? TechnicianId,
    int Received,
    int Delivered,
    int Late,
    decimal FinalCostSum,
    long SecondsToDeliverySum,
    int OpenNow);

public sealed record TechnicianName(int Id, string FullName, bool IsActive);

/// <summary>Qué fecha selecciona las órdenes de una exportación.</summary>
public enum ReportExportBasis
{
    /// <summary>Órdenes recibidas en el período (fecha de recepción).</summary>
    Received,

    /// <summary>Órdenes entregadas en el período (fecha de entrega).</summary>
    Delivered,
}

/// <summary>
/// Una orden exportada. Solo los datos necesarios para el análisis: sin teléfonos ni correos de clientes o técnicos, ni
/// datos de cuentas, historial o auditoría.
/// </summary>
public sealed record ReportExportRow(
    int Id,
    DateTime ReceivedAtUtc,
    ServiceOrderStatus Status,
    ServiceOrderPriority Priority,
    string CustomerName,
    DeviceType DeviceType,
    string DeviceBrand,
    string? DeviceModel,
    string? SerialNumber,
    string? TechnicianName,
    string ReportedIssue,
    DateTime? PromisedAtUtc,
    DateTime? DeliveredAtUtc,
    decimal? EstimatedCost,
    decimal? FinalCost)
{
    public string Number => ServiceOrder.FormatNumber(Id);
}

/// <summary>Resultado de una exportación: las filas, o el aviso de que superan el máximo permitido.</summary>
public sealed record ReportExport(IReadOnlyList<ReportExportRow> Rows, bool TooLarge, int MaxRows);

/// <summary>Una cantidad con su parte del total (para las distribuciones).</summary>
public sealed record ReportShare<TKey>(TKey Key, int Count, int Total)
{
    /// <summary>Porcentaje entero del total; 0 si no hay datos.</summary>
    public int Percent => Total == 0 ? 0 : (int)Math.Round(Count * 100m / Total, MidpointRounding.AwayFromZero);
}

/// <summary>Un tramo de la serie (un día, una semana o un mes local, recortado al período).</summary>
/// <param name="Revenue">Ingresos registrados: suma de los costos finales de las órdenes entregadas en el tramo.</param>
public sealed record ReportSeriesPoint(DateOnly Start, DateOnly End, int Received, int Delivered, decimal Revenue);

/// <summary>Comparación por prioridad: recibidas en el período y entregas del período con sus tiempos.</summary>
public sealed record PriorityComparison(
    ServiceOrderPriority Priority,
    int Received,
    int Delivered,
    int Late,
    int WithPromise,
    double? AverageHoursToDelivery)
{
    /// <summary>Porcentaje de entregas a tiempo entre las que tenían fecha comprometida; <c>null</c> sin datos.</summary>
    public int? OnTimePercent => ReportMath.Percent(WithPromise - Late, WithPromise);
}

/// <summary>Actividad de un técnico en el período, con su nombre.</summary>
/// <param name="TechnicianId"><c>null</c> para las órdenes sin técnico asignado.</param>
public sealed record TechnicianActivity(
    int? TechnicianId,
    string Name,
    bool IsActive,
    int Received,
    int Delivered,
    int Late,
    decimal FinalCostSum,
    double? AverageHoursToDelivery,
    int OpenNow)
{
    public int? OnTimePercent => ReportMath.Percent(Delivered - Late, Delivered);
}

/// <summary>
/// Reporte operativo de un período, todo calculado por la base con las órdenes registradas. Cada valor que no puede
/// calcularse por falta de datos es <c>null</c>: la interfaz muestra "Sin datos" en lugar de un promedio inventado.
/// </summary>
/// <param name="Received">Órdenes recibidas en el período (fecha de recepción).</param>
/// <param name="Delivered">Órdenes entregadas en el período (fecha de entrega).</param>
/// <param name="Cancelled">Órdenes canceladas en el período (fecha del cambio a "Cancelada" en su historial).</param>
/// <param name="Revenue">
/// Ingresos registrados: suma de los costos finales de las entregas del período. No son cobros confirmados: FixTrack no
/// tiene un módulo de pagos.
/// </param>
public sealed record OperationsReport(
    ReportFilter Filter,
    int Received,
    int Delivered,
    int Cancelled,
    int DeliveredWithPromise,
    int DeliveredLate,
    double? AverageHoursToDelivery,
    double? AverageHoursToReady,
    int ReadyMeasured,
    decimal Revenue,
    decimal? AverageFinalCost,
    int DeliveredWithEstimate,
    decimal EstimatedCostSum,
    decimal FinalCostSumWithEstimate,
    IReadOnlyList<ReportShare<ServiceOrderStatus>> ReceivedByStatus,
    IReadOnlyList<PriorityComparison> Priorities,
    IReadOnlyList<ReportSeriesPoint> Series,
    IReadOnlyList<TechnicianActivity> Technicians,
    DateTime CalculatedAtUtc)
{
    public ReportPeriod Period => Filter.Period;

    /// <summary>¿Hubo alguna orden recibida, entregada o cancelada en el período?</summary>
    public bool HasActivity => Received + Delivered + Cancelled > 0;

    /// <summary>Entregas a tiempo entre las que tenían fecha comprometida, en porcentaje; <c>null</c> sin datos.</summary>
    public int? OnTimePercent => ReportMath.Percent(DeliveredWithPromise - DeliveredLate, DeliveredWithPromise);

    /// <summary>Costo final menos costo estimado, en las entregas que tenían ambos; <c>null</c> sin datos.</summary>
    public decimal? CostVariance => DeliveredWithEstimate == 0 ? null : FinalCostSumWithEstimate - EstimatedCostSum;

    /// <summary>Variación respecto de lo estimado, en porcentaje con un decimal; <c>null</c> si no se puede calcular.</summary>
    public decimal? CostVariancePercent =>
        CostVariance is { } variance && EstimatedCostSum > 0 ? Math.Round(variance * 100m / EstimatedCostSum, 1, MidpointRounding.AwayFromZero) : null;
}

internal static class ReportMath
{
    /// <summary>Porcentaje entero de <paramref name="part"/> sobre <paramref name="total"/>; <c>null</c> si el total es 0.</summary>
    public static int? Percent(int part, int total) =>
        total == 0 ? null : (int)Math.Round(part * 100m / total, MidpointRounding.AwayFromZero);

    /// <summary>Promedio en horas de una suma de segundos; <c>null</c> si no hay elementos.</summary>
    public static double? AverageHours(long secondsSum, int count) => count == 0 ? null : secondsSum / 3600d / count;
}
