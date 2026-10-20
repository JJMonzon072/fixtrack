using FixTrack.Application.Devices;

namespace FixTrack.Application.Dashboard;

/// <summary>Consultas del panel. Lo implementa la capa de infraestructura con agregaciones en SQL.</summary>
public interface IDashboardRepository
{
    /// <param name="nowUtc">Momento de referencia para las órdenes vencidas.</param>
    /// <param name="recentSinceUtc">Desde cuándo una orden cuenta como recibida recientemente.</param>
    Task<OrderCounts> CountOrdersAsync(DateTime nowUtc, DateTime recentSinceUtc, CancellationToken cancellationToken);

    /// <summary>Órdenes abiertas cuya entrega comprometida todavía no pasó, de la más cercana a la más lejana.</summary>
    Task<IReadOnlyList<ServiceOrderSummary>> ListUpcomingDeliveriesAsync(DateTime nowUtc, int take, CancellationToken cancellationToken);

    /// <summary>Órdenes recibidas más recientemente, de cualquier estado.</summary>
    Task<IReadOnlyList<ServiceOrderSummary>> ListRecentlyReceivedAsync(int take, CancellationToken cancellationToken);
}
