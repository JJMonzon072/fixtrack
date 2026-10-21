namespace FixTrack.Application.Reports;

/// <summary>
/// Consultas del centro de reportes. Las implementa la infraestructura con agregaciones en SQL: ninguna carga las órdenes
/// en memoria y la cantidad de consultas no depende de la cantidad de órdenes.
/// </summary>
public interface IReportRepository
{
    /// <summary>Recibidas en el período, agrupadas por estado actual y prioridad.</summary>
    Task<IReadOnlyList<ReceivedGroup>> CountReceivedAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Entregadas en el período, agrupadas por prioridad, con sus sumas.</summary>
    Task<IReadOnlyList<DeliveredGroup>> SumDeliveredAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Tiempo hasta el último paso a "Lista para entrega" de las entregadas en el período.</summary>
    Task<ReadyTimeTotals> SumTimeToReadyAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Canceladas en el período, según la fecha del cambio a "Cancelada" en el historial.</summary>
    Task<int> CountCancelledAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Recibidas por día local del período.</summary>
    Task<IReadOnlyList<DailyTotal>> CountReceivedByDayAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Entregadas por día local del período, con la suma de sus costos finales.</summary>
    Task<IReadOnlyList<DailyTotal>> SumDeliveredByDayAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>Actividad por técnico (y de las órdenes sin técnico) en una sola agregación.</summary>
    Task<IReadOnlyList<TechnicianActivityRow>> ListTechnicianActivityAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<TechnicianName>> ListTechnicianNamesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Órdenes del período para exportar, ordenadas por la fecha elegida. Devuelve como máximo <paramref name="take"/>
    /// filas: quien llama pide una más que el máximo para saber si el resultado lo supera.
    /// </summary>
    Task<IReadOnlyList<ReportExportRow>> ListForExportAsync(
        ReportFilter filter,
        ReportExportBasis basis,
        int take,
        CancellationToken cancellationToken);
}
