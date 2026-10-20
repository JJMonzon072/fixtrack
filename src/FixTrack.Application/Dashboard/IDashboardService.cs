namespace FixTrack.Application.Dashboard;

/// <summary>Indicadores, acciones recomendadas y listas del panel general.</summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetAsync(CancellationToken cancellationToken);
}
