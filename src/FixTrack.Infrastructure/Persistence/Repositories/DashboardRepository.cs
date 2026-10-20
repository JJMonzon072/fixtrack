using FixTrack.Application.Dashboard;
using FixTrack.Application.Devices;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>Consultas del panel: los indicadores se calculan con una sola agregación en SQL.</summary>
internal sealed class DashboardRepository(FixTrackDbContext db) : IDashboardRepository
{
    public async Task<OrderCounts> CountOrdersAsync(DateTime nowUtc, DateTime recentSinceUtc, CancellationToken cancellationToken) =>
        await db.ServiceOrders
            .AsNoTracking()
            .GroupBy(order => 1)
            .Select(group => new OrderCounts(
                group.Count(order => order.Status == ServiceOrderStatus.Received),
                group.Count(order => order.Status == ServiceOrderStatus.Diagnosing),
                group.Count(order => order.Status == ServiceOrderStatus.InRepair),
                group.Count(order => order.Status == ServiceOrderStatus.AwaitingParts),
                group.Count(order => order.Status == ServiceOrderStatus.ReadyForPickup),
                group.Count(order => order.Status == ServiceOrderStatus.Delivered),
                group.Count(order => order.Status == ServiceOrderStatus.Cancelled),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.PromisedAtUtc < nowUtc),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.TechnicianId == null),
                group.Count(order => order.ReceivedAtUtc >= recentSinceUtc),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.Priority == ServiceOrderPriority.Low),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.Priority == ServiceOrderPriority.Normal),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.Priority == ServiceOrderPriority.High),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.Priority == ServiceOrderPriority.Urgent)))
            .SingleOrDefaultAsync(cancellationToken) ?? OrderCounts.Empty;

    public async Task<IReadOnlyList<ServiceOrderSummary>> ListUpcomingDeliveriesAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        await db.ServiceOrders
            .AsNoTracking()
            .WhereOpen()
            .Where(order => order.PromisedAtUtc >= nowUtc)
            .OrderBy(order => order.PromisedAtUtc)
            .ThenBy(order => order.Id)
            .Take(take)
            .Select(ServiceOrderQueries.Summary)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ServiceOrderSummary>> ListRecentlyReceivedAsync(int take, CancellationToken cancellationToken) =>
        await db.ServiceOrders
            .AsNoTracking()
            .OrderByDescending(order => order.ReceivedAtUtc)
            .ThenByDescending(order => order.Id)
            .Take(take)
            .Select(ServiceOrderQueries.Summary)
            .ToListAsync(cancellationToken);
}
