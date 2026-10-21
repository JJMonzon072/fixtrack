namespace FixTrack.Application.Reports;

/// <summary>Centro de reportes: indicadores, distribuciones, series y exportación de un período.</summary>
public interface IReportService
{
    Task<OperationsReport> GetAsync(ReportFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Órdenes del período para exportar. Si son más de <paramref name="maxRows"/>, no devuelve ninguna e indica que el
    /// resultado es demasiado grande: una exportación parcial podría tomarse por completa.
    /// </summary>
    Task<ReportExport> ExportAsync(ReportFilter filter, ReportExportBasis basis, int maxRows, CancellationToken cancellationToken);
}
