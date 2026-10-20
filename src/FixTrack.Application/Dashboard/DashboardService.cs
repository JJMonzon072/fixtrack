using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Dashboard;

internal sealed class DashboardService(
    IDashboardRepository dashboard,
    IServiceOrderService orders,
    ITechnicianRepository technicians,
    TimeProvider clock) : IDashboardService
{
    internal const int RecentDays = 7;
    internal const int ItemsPerList = 5;

    public async Task<DashboardSummary> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var counts = await dashboard.CountOrdersAsync(now, now.AddDays(-RecentDays), cancellationToken);
        if (counts.Total == 0)
        {
            // Sin órdenes no hay nada más que consultar: el panel muestra ceros y estados vacíos.
            return new DashboardSummary(counts, RecentDays, [], [], [], await ActiveWorkloadAsync(cancellationToken), now);
        }

        // Las órdenes de mayor puntuación de las tres listas: la base elige solo las primeras.
        var recommended = await orders.GetMostUrgentAsync(ItemsPerList, cancellationToken);

        return new DashboardSummary(
            counts,
            RecentDays,
            recommended,
            await dashboard.ListUpcomingDeliveriesAsync(now, ItemsPerList, cancellationToken),
            await dashboard.ListRecentlyReceivedAsync(ItemsPerList, cancellationToken),
            await ActiveWorkloadAsync(cancellationToken),
            now);
    }

    private async Task<IReadOnlyList<TechnicianWorkload>> ActiveWorkloadAsync(CancellationToken cancellationToken) =>
        [.. (await technicians.ListWorkloadAsync(cancellationToken)).Where(technician => technician.IsActive)];
}
