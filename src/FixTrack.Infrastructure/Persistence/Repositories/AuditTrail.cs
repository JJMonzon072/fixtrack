using FixTrack.Application.Auditing;
using FixTrack.Application.Common;
using FixTrack.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

internal sealed class AuditTrail(FixTrackDbContext db) : IAuditTrail
{
    public async Task<AuditSearchResult> SearchAsync(AuditSearch search, CancellationToken cancellationToken)
    {
        // Una sola consulta agrupada para los contadores de las pestañas.
        var byType = await db.AuditEvents.AsNoTracking()
            .GroupBy(audit => audit.EntityType)
            .Select(group => new { EntityType = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.EntityType, row => row.Count, cancellationToken);
        var counts = Enum.GetValues<AuditArea>().ToDictionary(
            area => area,
            area => area == AuditArea.All ? byType.Values.Sum() : byType.GetValueOrDefault(EntityTypeOf(area)));

        var events = db.AuditEvents.AsNoTracking();
        if (search.Area != AuditArea.All)
        {
            var entityType = EntityTypeOf(search.Area);
            events = events.Where(audit => audit.EntityType == entityType);
        }

        var total = counts[search.Area];
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)AuditSearch.PageSize));
        var page = Math.Clamp(search.Page, 1, lastPage);
        var items = await events
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Skip((page - 1) * AuditSearch.PageSize)
            .Take(AuditSearch.PageSize)
            .Select(audit => new AuditEntry(audit.OccurredAtUtc, audit.UserName, audit.Action, audit.EntityType, audit.EntityId, audit.Summary))
            .ToListAsync(cancellationToken);

        return new AuditSearchResult(new PagedResult<AuditEntry>(items, page, AuditSearch.PageSize, total), counts);
    }

    private static string EntityTypeOf(AuditArea area) => area switch
    {
        AuditArea.Orders => AuditActions.Order,
        AuditArea.Technicians => AuditActions.Technician,
        AuditArea.Customers => AuditActions.Customer,
        _ => AuditActions.Account,
    };
}
