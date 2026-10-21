using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.ReadApi;
using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Listados de la API de consulta: filtros, conteo, orden estable (el Id desempata) y página, todo en SQL con proyecciones
/// a los modelos de la aplicación. Una página fuera del total devuelve una lista vacía con el total real.
/// </summary>
internal sealed class ReadApiQueries(FixTrackDbContext db) : IReadApiQueries
{
    public async Task<PagedResult<ServiceOrderListItem>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken)
    {
        var orders = db.ServiceOrders.AsNoTracking();
        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (query.Priority is { } priority)
        {
            orders = orders.Where(order => order.Priority == priority);
        }

        if (query.UnassignedOnly)
        {
            orders = orders.Where(order => order.TechnicianId == null);
        }
        else if (query.TechnicianId is { } technicianId)
        {
            orders = orders.Where(order => order.TechnicianId == technicianId);
        }

        if (query.DeviceId is { } deviceId)
        {
            orders = orders.Where(order => order.DeviceId == deviceId);
        }

        if (query.CustomerId is { } customerId)
        {
            orders = orders.Where(order => order.Device!.CustomerId == customerId);
        }

        if (query.ReceivedFromUtc is { } from)
        {
            orders = orders.Where(order => order.ReceivedAtUtc >= from);
        }

        if (query.ReceivedToUtc is { } to)
        {
            orders = orders.Where(order => order.ReceivedAtUtc < to);
        }

        var total = await orders.CountAsync(cancellationToken);
        var items = await Sort(orders, query.Sort, query.Descending)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ServiceOrderQueries.ListItem)
            .ToListAsync(cancellationToken);

        return new PagedResult<ServiceOrderListItem>(items, query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<DeviceListItem>> ListDevicesAsync(DeviceListQuery query, CancellationToken cancellationToken)
    {
        var devices = db.Devices.AsNoTracking();
        if (query.Type is { } type)
        {
            devices = devices.Where(device => device.Type == type);
        }

        if (query.CustomerId is { } customerId)
        {
            devices = devices.Where(device => device.CustomerId == customerId);
        }

        var total = await devices.CountAsync(cancellationToken);
        var ordered = (query.Sort, query.Descending) switch
        {
            (DeviceSortField.Brand, false) => devices.OrderBy(device => device.Brand).ThenBy(device => device.Model).ThenBy(device => device.Id),
            (DeviceSortField.Brand, true) => devices.OrderByDescending(device => device.Brand).ThenByDescending(device => device.Model).ThenByDescending(device => device.Id),
            (_, false) => devices.OrderBy(device => device.CreatedAtUtc).ThenBy(device => device.Id),
            _ => devices.OrderByDescending(device => device.CreatedAtUtc).ThenByDescending(device => device.Id),
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(device => new DeviceListItem(
                device.Id,
                device.Type,
                device.Brand,
                device.Model,
                device.SerialNumber,
                device.CustomerId,
                device.Customer!.FullName,
                device.Customer.IsActive,
                db.ServiceOrders.Count(order => order.DeviceId == device.Id),
                device.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<DeviceListItem>(items, query.Page, query.PageSize, total);
    }

    private static IOrderedQueryable<ServiceOrder> Sort(IQueryable<ServiceOrder> orders, OrderSortField field, bool descending) => (field, descending) switch
    {
        // Sin fecha comprometida, al final en ambos sentidos.
        (OrderSortField.Promised, false) => orders.OrderBy(order => order.PromisedAtUtc == null).ThenBy(order => order.PromisedAtUtc).ThenBy(order => order.Id),
        (OrderSortField.Promised, true) => orders.OrderBy(order => order.PromisedAtUtc == null).ThenByDescending(order => order.PromisedAtUtc).ThenByDescending(order => order.Id),
        (OrderSortField.Priority, false) => orders.OrderBy(order => order.Priority).ThenBy(order => order.Id),
        (OrderSortField.Priority, true) => orders.OrderByDescending(order => order.Priority).ThenByDescending(order => order.Id),
        (OrderSortField.Number, false) => orders.OrderBy(order => order.Id),
        (OrderSortField.Number, true) => orders.OrderByDescending(order => order.Id),
        (_, false) => orders.OrderBy(order => order.ReceivedAtUtc).ThenBy(order => order.Id),
        _ => orders.OrderByDescending(order => order.ReceivedAtUtc).ThenByDescending(order => order.Id),
    };
}
