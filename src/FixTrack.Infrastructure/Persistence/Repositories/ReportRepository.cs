using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas del centro de reportes. Cada método es una sola agregación en SQL (GROUP BY, COUNT y SUM condicionales) sobre
/// las tablas existentes; las fechas del período viajan como parámetros y son intervalos [inicio, fin) en UTC. Las series
/// diarias se agrupan por día local sumando el desfase de la zona a las fechas UTC, una consulta por tramo de desfase
/// constante (uno solo en zonas sin horario de verano, como America/Guatemala).
/// </summary>
internal sealed class ReportRepository(FixTrackDbContext db) : IReportRepository
{
    public async Task<IReadOnlyList<ReceivedGroup>> CountReceivedAsync(ReportFilter filter, CancellationToken cancellationToken) =>
        await ReceivedIn(filter)
            .GroupBy(order => new { order.Status, order.Priority })
            .Select(group => new ReceivedGroup(group.Key.Status, group.Key.Priority, group.Count()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeliveredGroup>> SumDeliveredAsync(ReportFilter filter, CancellationToken cancellationToken) =>
        await DeliveredIn(filter)
            .GroupBy(order => order.Priority)
            .Select(group => new DeliveredGroup(
                group.Key,
                group.Count(),
                group.Count(order => order.PromisedAtUtc != null),
                group.Count(order => order.PromisedAtUtc != null && order.DeliveredAtUtc > order.PromisedAtUtc),
                group.Sum(order => order.FinalCost) ?? 0m,
                group.Count(order => order.EstimatedCost != null),
                group.Sum(order => order.EstimatedCost) ?? 0m,
                group.Sum(order => order.EstimatedCost != null ? order.FinalCost : null) ?? 0m,
                group.Sum(order => ReportSql.UnixSeconds(order.DeliveredAtUtc!.Value) - ReportSql.UnixSeconds(order.ReceivedAtUtc))))
            .ToListAsync(cancellationToken);

    public async Task<ReadyTimeTotals> SumTimeToReadyAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        // Una orden puede volver de "Lista para entrega" a reparación: cuenta el último paso a ese estado.
        var readyAt = db.ServiceOrderStatusHistory
            .Where(change => change.ToStatus == ServiceOrderStatus.ReadyForPickup)
            .GroupBy(change => change.ServiceOrderId)
            .Select(group => new { OrderId = group.Key, ReadyAtUtc = group.Max(change => change.ChangedAtUtc) });

        var totals = await DeliveredIn(filter)
            .Join(readyAt, order => order.Id, ready => ready.OrderId, (order, ready) => new { order.ReceivedAtUtc, ready.ReadyAtUtc })
            .GroupBy(pair => 1)
            .Select(group => new ReadyTimeTotals(
                group.Count(),
                group.Sum(pair => ReportSql.UnixSeconds(pair.ReadyAtUtc) - ReportSql.UnixSeconds(pair.ReceivedAtUtc))))
            .SingleOrDefaultAsync(cancellationToken);
        return totals ?? ReadyTimeTotals.Empty;
    }

    public async Task<int> CountCancelledAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var (start, end) = (filter.Period.StartUtc, filter.Period.EndUtc);
        var cancelledInPeriod = db.ServiceOrderStatusHistory
            .Where(change => change.ToStatus == ServiceOrderStatus.Cancelled && change.ChangedAtUtc >= start && change.ChangedAtUtc < end)
            .Select(change => change.ServiceOrderId);

        return await Filtered(filter)
            .Where(order => order.Status == ServiceOrderStatus.Cancelled && cancelledInPeriod.Contains(order.Id))
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyTotal>> CountReceivedByDayAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var days = new List<DailyTotal>();
        foreach (var segment in filter.Period.OffsetSegments())
        {
            var (start, end, offset) = (segment.StartUtc, segment.EndUtc, segment.OffsetMinutes);
            var rows = await Filtered(filter)
                .Where(order => order.ReceivedAtUtc >= start && order.ReceivedAtUtc < end)
                .GroupBy(order => order.ReceivedAtUtc.AddMinutes(offset).Date)
                .Select(group => new { Day = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            days.AddRange(rows.Select(row => new DailyTotal(DateOnly.FromDateTime(row.Day), row.Count, 0m)));
        }

        return days;
    }

    public async Task<IReadOnlyList<DailyTotal>> SumDeliveredByDayAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var days = new List<DailyTotal>();
        foreach (var segment in filter.Period.OffsetSegments())
        {
            var (start, end, offset) = (segment.StartUtc, segment.EndUtc, segment.OffsetMinutes);
            var rows = await Filtered(filter)
                .Where(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end)
                .GroupBy(order => order.DeliveredAtUtc!.Value.AddMinutes(offset).Date)
                .Select(group => new { Day = group.Key, Count = group.Count(), Amount = group.Sum(order => order.FinalCost) ?? 0m })
                .ToListAsync(cancellationToken);
            days.AddRange(rows.Select(row => new DailyTotal(DateOnly.FromDateTime(row.Day), row.Count, row.Amount)));
        }

        return days;
    }

    public async Task<IReadOnlyList<TechnicianActivityRow>> ListTechnicianActivityAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var (start, end) = (filter.Period.StartUtc, filter.Period.EndUtc);
        return await Filtered(filter)
            .GroupBy(order => order.TechnicianId)
            .Select(group => new TechnicianActivityRow(
                group.Key,
                group.Count(order => order.ReceivedAtUtc >= start && order.ReceivedAtUtc < end),
                group.Count(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end),
                group.Count(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end
                    && order.PromisedAtUtc != null && order.DeliveredAtUtc > order.PromisedAtUtc),
                group.Sum(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end
                    ? order.FinalCost
                    : null) ?? 0m,
                group.Sum(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end
                    ? ReportSql.UnixSeconds(order.DeliveredAtUtc!.Value) - ReportSql.UnixSeconds(order.ReceivedAtUtc)
                    : 0L),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TechnicianName>> ListTechnicianNamesAsync(CancellationToken cancellationToken) =>
        await db.Technicians
            .AsNoTracking()
            .Select(technician => new TechnicianName(technician.Id, technician.FullName, technician.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportExportRow>> ListForExportAsync(
        ReportFilter filter,
        ReportExportBasis basis,
        int take,
        CancellationToken cancellationToken)
    {
        var orders = basis == ReportExportBasis.Delivered
            ? DeliveredIn(filter).OrderBy(order => order.DeliveredAtUtc).ThenBy(order => order.Id)
            : ReceivedIn(filter).OrderBy(order => order.ReceivedAtUtc).ThenBy(order => order.Id);

        return await orders
            .Take(take)
            .Select(order => new ReportExportRow(
                order.Id,
                order.ReceivedAtUtc,
                order.Status,
                order.Priority,
                order.Device!.Customer!.FullName,
                order.Device.Type,
                order.Device.Brand,
                order.Device.Model,
                order.Device.SerialNumber,
                order.Technician != null ? order.Technician.FullName : null,
                order.ReportedIssue,
                order.PromisedAtUtc,
                order.DeliveredAtUtc,
                order.EstimatedCost,
                order.FinalCost))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Órdenes que cumplen los filtros de estado, prioridad y técnico, sin seguimiento de cambios.</summary>
    private IQueryable<ServiceOrder> Filtered(ReportFilter filter)
    {
        var orders = db.ServiceOrders.AsNoTracking();
        if (filter.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (filter.Priority is { } priority)
        {
            orders = orders.Where(order => order.Priority == priority);
        }

        if (filter.UnassignedOnly)
        {
            orders = orders.Where(order => order.TechnicianId == null);
        }
        else if (filter.TechnicianId is { } technicianId)
        {
            orders = orders.Where(order => order.TechnicianId == technicianId);
        }

        return orders;
    }

    /// <summary>Recibidas en el período: fecha de recepción en [inicio, fin).</summary>
    private IQueryable<ServiceOrder> ReceivedIn(ReportFilter filter)
    {
        var (start, end) = (filter.Period.StartUtc, filter.Period.EndUtc);
        return Filtered(filter).Where(order => order.ReceivedAtUtc >= start && order.ReceivedAtUtc < end);
    }

    /// <summary>Entregadas en el período: fecha de entrega en [inicio, fin).</summary>
    private IQueryable<ServiceOrder> DeliveredIn(ReportFilter filter)
    {
        var (start, end) = (filter.Period.StartUtc, filter.Period.EndUtc);
        return Filtered(filter)
            .Where(order => order.Status == ServiceOrderStatus.Delivered && order.DeliveredAtUtc >= start && order.DeliveredAtUtc < end);
    }
}
