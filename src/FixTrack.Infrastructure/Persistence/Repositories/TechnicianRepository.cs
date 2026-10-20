using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>Consultas de técnicos con EF Core. Todas las búsquedas se envían parametrizadas.</summary>
internal sealed class TechnicianRepository(FixTrackDbContext db) : ITechnicianRepository
{
    private const int OpenOrdersShown = 20;

    public async Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search, CancellationToken cancellationToken)
    {
        var matching = db.Technicians.AsNoTracking();
        if (search.Text is { } text)
        {
            var pattern = LikePatterns.Contains(text);
            matching = matching.Where(technician =>
                EF.Functions.Like(technician.FullName, pattern, LikePatterns.Escape)
                || EF.Functions.Like(technician.Specialty, pattern, LikePatterns.Escape)
                || (technician.Phone != null && EF.Functions.Like(technician.Phone, pattern, LikePatterns.Escape))
                || (technician.Email != null && EF.Functions.Like(technician.Email, pattern, LikePatterns.Escape)));
        }

        var counts = await matching
            .GroupBy(technician => technician.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var activeCount = counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0;
        var inactiveCount = counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0;

        var (filtered, total) = search.Status switch
        {
            StatusFilter.Inactive => (matching.Where(technician => !technician.IsActive), inactiveCount),
            StatusFilter.All => (matching, activeCount + inactiveCount),
            _ => (matching.Where(technician => technician.IsActive), activeCount),
        };

        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)TechnicianSearch.PageSize));
        var page = Math.Min(search.Page, lastPage);

        var items = await filtered
            .OrderBy(technician => technician.FullName)
            .ThenBy(technician => technician.Id)
            .Skip((page - 1) * TechnicianSearch.PageSize)
            .Take(TechnicianSearch.PageSize)
            .Select(technician => new TechnicianListItem(
                technician.Id,
                technician.FullName,
                technician.Specialty,
                technician.Phone,
                technician.Email,
                technician.IsActive,
                db.ServiceOrders.Count(order => order.TechnicianId == technician.Id
                    && order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled),
                technician.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new TechnicianSearchResult(
            new PagedResult<TechnicianListItem>(items, page, TechnicianSearch.PageSize, total),
            activeCount,
            inactiveCount);
    }

    public Task<TechnicianDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
        db.Technicians
            .AsNoTracking()
            .Where(technician => technician.Id == id)
            .Select(technician => new TechnicianDetails(
                technician.Id,
                technician.FullName,
                technician.Specialty,
                technician.Phone,
                technician.Email,
                technician.IsActive,
                technician.CreatedAtUtc,
                technician.UpdatedAtUtc,
                technician.DeactivatedAtUtc,
                db.ServiceOrders.Count(order => order.TechnicianId == technician.Id
                    && order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled),
                db.ServiceOrders.Count(order => order.TechnicianId == technician.Id),
                db.ServiceOrders
                    .Where(order => order.TechnicianId == technician.Id
                        && order.Status != ServiceOrderStatus.Delivered
                        && order.Status != ServiceOrderStatus.Cancelled)
                    .OrderBy(order => order.PromisedAtUtc == null)
                    .ThenBy(order => order.PromisedAtUtc)
                    .Take(OpenOrdersShown)
                    .Select(order => new ServiceOrderSummary(order.Id, order.Status, order.Priority, order.ReceivedAtUtc, order.PromisedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Technician?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.Technicians.SingleOrDefaultAsync(technician => technician.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, int? excludingTechnicianId, CancellationToken cancellationToken) =>
        db.Technicians.AnyAsync(
            technician => technician.Email == email && (excludingTechnicianId == null || technician.Id != excludingTechnicianId),
            cancellationToken);

    public Task<int> CountOpenOrdersAsync(int technicianId, CancellationToken cancellationToken) =>
        db.ServiceOrders.CountAsync(
            order => order.TechnicianId == technicianId
                && order.Status != ServiceOrderStatus.Delivered
                && order.Status != ServiceOrderStatus.Cancelled,
            cancellationToken);

    public void Add(Technician technician) => db.Technicians.Add(technician);
}
