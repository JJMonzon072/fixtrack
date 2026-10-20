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

    public async Task<TechnicianDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        var details = await db.Technicians
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
                db.ServiceOrders.WhereOpen().Count(order => order.TechnicianId == technician.Id),
                db.ServiceOrders.Count(order => order.TechnicianId == technician.Id),
                Array.Empty<ServiceOrderSummary>(),
                technician.Version))
            .SingleOrDefaultAsync(cancellationToken);
        if (details is null)
        {
            return null;
        }

        // Las órdenes abiertas que vencen antes aparecen primero; las que no tienen fecha, al final.
        var openOrders = await db.ServiceOrders
            .AsNoTracking()
            .WhereOpen()
            .Where(order => order.TechnicianId == id)
            .OrderBy(order => order.PromisedAtUtc == null)
            .ThenBy(order => order.PromisedAtUtc)
            .ThenBy(order => order.Id)
            .Take(OpenOrdersShown)
            .Select(ServiceOrderQueries.Summary)
            .ToListAsync(cancellationToken);
        return details with { OpenOrders = openOrders };
    }

    public Task<Technician?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.Technicians.SingleOrDefaultAsync(technician => technician.Id == id, cancellationToken);

    /// <remarks>
    /// En SQL Server se lee con <c>WITH (UPDLOCK, ROWLOCK)</c>: el bloqueo de actualización sobre la fila del
    /// técnico se conserva hasta el final de la transacción y es incompatible con otro UPDLOCK sobre la misma
    /// fila. Así, una asignación y una desactivación del mismo técnico nunca se intercalan: la segunda espera
    /// a que la primera confirme y después lee el estado actualizado. Las lecturas normales (listados,
    /// detalle) no se bloquean, porque el bloqueo U es compatible con los bloqueos compartidos.
    /// SQLite, que solo se usa en las pruebas, no admite estas sugerencias; allí cada transacción de
    /// escritura ya bloquea la base completa.
    /// </remarks>
    public Task<Technician?> FindForAssignmentAsync(int id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("El bloqueo del técnico requiere una transacción activa (IUnitOfWork.ExecuteInTransactionAsync).");
        }

        return db.Database.IsSqlServer()
            ? db.Technicians
                .FromSql($"SELECT * FROM [Technicians] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
                .SingleOrDefaultAsync(cancellationToken)
            : FindAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<TechnicianWorkload>> ListWorkloadAsync(CancellationToken cancellationToken) =>
        await db.Technicians
            .AsNoTracking()
            .OrderBy(technician => technician.FullName)
            .ThenBy(technician => technician.Id)
            .Select(technician => new TechnicianWorkload(
                technician.Id,
                technician.FullName,
                technician.Specialty,
                technician.IsActive,
                db.ServiceOrders.Count(order => order.TechnicianId == technician.Id
                    && order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled)))
            .ToListAsync(cancellationToken);

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
