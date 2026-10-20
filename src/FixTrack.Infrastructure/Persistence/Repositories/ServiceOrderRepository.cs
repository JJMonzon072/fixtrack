using System.Globalization;
using System.Text.RegularExpressions;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Auditing;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas de órdenes con EF Core. Filtros, conteos, orden y paginación se resuelven en SQL:
/// solo se leen las filas de la página solicitada, con sus datos relacionados en la misma consulta.
/// </summary>
internal sealed partial class ServiceOrderRepository(FixTrackDbContext db) : IServiceOrderRepository
{
    public async Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var matching = Filter(db.ServiceOrders.AsNoTracking(), search);

        // Una sola consulta de agregación para las cantidades de cada vista.
        var counts = await matching
            .GroupBy(order => 1)
            .Select(group => new ServiceOrderViewCounts(
                group.Count(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled),
                group.Count(order => order.Status != ServiceOrderStatus.Delivered
                    && order.Status != ServiceOrderStatus.Cancelled
                    && order.PromisedAtUtc < nowUtc),
                group.Count(order => order.Status == ServiceOrderStatus.Delivered || order.Status == ServiceOrderStatus.Cancelled)))
            .SingleOrDefaultAsync(cancellationToken) ?? new ServiceOrderViewCounts(0, 0, 0);

        var inView = search.View switch
        {
            ServiceOrderView.Open => matching.WhereOpen(),
            ServiceOrderView.Overdue => matching.WhereOpen().Where(order => order.PromisedAtUtc < nowUtc),
            ServiceOrderView.Closed => matching.Where(order =>
                order.Status == ServiceOrderStatus.Delivered || order.Status == ServiceOrderStatus.Cancelled),
            _ => matching,
        };

        int total;
        if (search.Status is { } status)
        {
            inView = inView.Where(order => order.Status == status);
            total = await inView.CountAsync(cancellationToken);
        }
        else
        {
            total = search.View switch
            {
                ServiceOrderView.Open => counts.Open,
                ServiceOrderView.Overdue => counts.Overdue,
                ServiceOrderView.Closed => counts.Closed,
                _ => counts.All,
            };
        }

        // Las órdenes abiertas se ordenan por la entrega comprometida (las que vencen antes, primero; sin fecha, al
        // final). Las cerradas y el listado completo, de la más reciente a la más antigua. El Id desempata siempre.
        var ordered = search.View is ServiceOrderView.Open or ServiceOrderView.Overdue
            ? inView
                .OrderBy(order => order.PromisedAtUtc == null)
                .ThenBy(order => order.PromisedAtUtc)
                .ThenBy(order => order.ReceivedAtUtc)
                .ThenBy(order => order.Id)
            : inView
                .OrderByDescending(order => order.ReceivedAtUtc)
                .ThenByDescending(order => order.Id);

        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)ServiceOrderSearch.PageSize));
        var page = Math.Min(search.Page, lastPage);

        var items = await ordered
            .Skip((page - 1) * ServiceOrderSearch.PageSize)
            .Take(ServiceOrderSearch.PageSize)
            .Select(order => new ServiceOrderListItem(
                order.Id,
                order.Status,
                order.Priority,
                order.Device!.CustomerId,
                order.Device.Customer!.FullName,
                order.DeviceId,
                order.Device.Type,
                order.Device.Brand,
                order.Device.Model,
                order.TechnicianId,
                order.Technician != null ? order.Technician.FullName : null,
                order.ReceivedAtUtc,
                order.PromisedAtUtc,
                order.DeliveredAtUtc))
            .ToListAsync(cancellationToken);

        return new ServiceOrderSearchResult(new PagedResult<ServiceOrderListItem>(items, page, ServiceOrderSearch.PageSize, total), counts);
    }

    public async Task<ServiceOrderDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        var details = await db.ServiceOrders
            .AsNoTracking()
            .Where(order => order.Id == id)
            .Select(order => new ServiceOrderDetails(
                order.Id,
                order.Status,
                order.Priority,
                order.ReportedIssue,
                order.IntakeNotes,
                order.Diagnosis,
                order.EstimatedCost,
                order.FinalCost,
                order.ReceivedAtUtc,
                order.PromisedAtUtc,
                order.DeliveredAtUtc,
                order.UpdatedAtUtc,
                order.Version,
                new CustomerOption(
                    order.Device!.Customer!.Id,
                    order.Device.Customer.FullName,
                    order.Device.Customer.Phone,
                    order.Device.Customer.Email,
                    order.Device.Customer.IsActive),
                new ServiceOrderDeviceInfo(order.Device.Id, order.Device.Type, order.Device.Brand, order.Device.Model, order.Device.SerialNumber),
                order.Technician == null
                    ? null
                    : new ServiceOrderTechnicianInfo(
                        order.Technician.Id,
                        order.Technician.FullName,
                        order.Technician.Specialty,
                        order.Technician.IsActive,
                        order.TechnicianAssignedAtUtc!.Value),
                order.StatusHistory
                    .OrderBy(change => change.ChangedAtUtc)
                    .ThenBy(change => change.Id)
                    .Select(change => new ServiceOrderHistoryEntry(change.FromStatus, change.ToStatus, change.ChangedAtUtc, change.Note, change.ChangedByName))
                    .ToList(),
                Array.Empty<ServiceOrderAssignmentEntry>(),
                Array.Empty<ServiceOrderActivityEntry>()))
            .SingleOrDefaultAsync(cancellationToken);
        if (details is null)
        {
            return null;
        }

        // Segunda consulta: dos colecciones en la misma multiplicarían las filas (historial × asignaciones).
        var assignments = await db.ServiceOrderAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ServiceOrderId == id)
            .OrderBy(assignment => assignment.AssignedAtUtc)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new ServiceOrderAssignmentEntry(
                db.Technicians.Where(technician => technician.Id == assignment.FromTechnicianId).Select(technician => technician.FullName).FirstOrDefault(),
                assignment.ToTechnician!.FullName,
                assignment.AssignedAtUtc,
                assignment.AssignedByName,
                assignment.Note))
            .ToListAsync(cancellationToken);

        // Tercera consulta: los cambios de planificación y diagnóstico están en la auditoría.
        var orderId = id.ToString(CultureInfo.InvariantCulture);
        var prefix = details.Number + ": ";
        var activity = await db.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.EntityType == AuditActions.Order && audit.EntityId == orderId
                && (audit.Action == AuditActions.PlanChanged || audit.Action == AuditActions.DiagnosisRecorded))
            .OrderBy(audit => audit.OccurredAtUtc)
            .ThenBy(audit => audit.Id)
            .Select(audit => new ServiceOrderActivityEntry(audit.Action, audit.OccurredAtUtc, audit.UserName, audit.Summary))
            .ToListAsync(cancellationToken);

        return details with
        {
            Assignments = assignments,
            Activity = [.. activity.Select(entry => entry.Summary.StartsWith(prefix, StringComparison.Ordinal) ? entry with { Summary = entry.Summary[prefix.Length..] } : entry)],
        };
    }

    public async Task<CustomerServiceOrders> ListByCustomerAsync(int customerId, int take, CancellationToken cancellationToken)
    {
        var orders = db.ServiceOrders.AsNoTracking().Where(order => order.Device!.CustomerId == customerId);
        var counts = await orders
            .GroupBy(order => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Open = group.Count(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (counts is null)
        {
            return new CustomerServiceOrders(0, 0, []);
        }

        var recent = await orders
            .OrderByDescending(order => order.ReceivedAtUtc)
            .ThenByDescending(order => order.Id)
            .Take(take)
            .Select(ServiceOrderQueries.Summary)
            .ToListAsync(cancellationToken);
        return new CustomerServiceOrders(counts.Total, counts.Open, recent);
    }

    public async Task<IReadOnlyList<OpenOrderSnapshot>> ListOpenAsync(int limit, CancellationToken cancellationToken) =>
        await db.ServiceOrders
            .AsNoTracking()
            .WhereOpen()
            .OrderBy(order => order.ReceivedAtUtc)
            .ThenBy(order => order.Id)
            .Take(limit)
            .Select(order => new OpenOrderSnapshot(
                order.Id,
                order.Status,
                order.Priority,
                order.ReceivedAtUtc,
                order.PromisedAtUtc,
                order.TechnicianId,
                order.Technician != null ? order.Technician.FullName : null,
                order.Device!.Customer!.FullName,
                order.Device.Type,
                order.Device.Brand,
                order.Device.Model))
            .ToListAsync(cancellationToken);

    public Task<ServiceOrder?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.ServiceOrders.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

    public Task<int?> FindIdByCreationRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.ServiceOrders.AsNoTracking()
            .Where(order => order.CreationRequestId == requestId)
            .Select(order => (int?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(ServiceOrder order) => db.ServiceOrders.Add(order);

    private static IQueryable<ServiceOrder> Filter(IQueryable<ServiceOrder> orders, ServiceOrderSearch search)
    {
        if (search.Priority is { } priority)
        {
            orders = orders.Where(order => order.Priority == priority);
        }

        if (search.CustomerId is { } customerId)
        {
            orders = orders.Where(order => order.Device!.CustomerId == customerId);
        }

        if (search.DeviceId is { } deviceId)
        {
            orders = orders.Where(order => order.DeviceId == deviceId);
        }

        if (search.UnassignedOnly)
        {
            orders = orders.Where(order => order.TechnicianId == null);
        }
        else if (search.TechnicianId is { } technicianId)
        {
            orders = orders.Where(order => order.TechnicianId == technicianId);
        }

        if (search.Text is not { } text)
        {
            return orders;
        }

        // En SQL Server los nombres respetan su intercalación: sin distinguir mayúsculas ni tildes.
        var pattern = LikePatterns.Contains(text);
        var number = ParseOrderNumber(text);
        return orders.Where(order =>
            order.Id == number
            || EF.Functions.Like(order.Device!.Customer!.FullName, pattern, LikePatterns.Escape)
            || EF.Functions.Like(order.Device.Brand, pattern, LikePatterns.Escape)
            || (order.Device.Model != null && EF.Functions.Like(order.Device.Model, pattern, LikePatterns.Escape))
            || EF.Functions.Like(order.Device.Brand + " " + (order.Device.Model ?? string.Empty), pattern, LikePatterns.Escape)
            || (order.Device.SerialNumber != null && EF.Functions.Like(order.Device.SerialNumber, pattern, LikePatterns.Escape))
            || (order.Technician != null && EF.Functions.Like(order.Technician.FullName, pattern, LikePatterns.Escape)));
    }

    /// <summary>Reconoce un número de orden escrito como "OS-000012", "os 12", "#12" o "12". Devuelve 0 si no lo es.</summary>
    private static int ParseOrderNumber(string text)
    {
        var match = OrderNumber().Match(text);
        return match.Success && int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    [GeneratedRegex(@"^(?:OS[-\s]?|#)?0*(?<id>[1-9]\d{0,8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderNumber();
}
