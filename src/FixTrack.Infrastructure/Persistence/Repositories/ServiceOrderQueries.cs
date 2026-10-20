using System.Linq.Expressions;
using FixTrack.Application.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>Consultas de órdenes compartidas por los repositorios, para no repetir filtros ni proyecciones.</summary>
internal static class ServiceOrderQueries
{
    /// <summary>Resumen de una orden con su equipo, cliente y técnico (una sola consulta con JOIN).</summary>
    public static readonly Expression<Func<ServiceOrder, ServiceOrderSummary>> Summary = order => new ServiceOrderSummary(
        order.Id,
        order.Status,
        order.Priority,
        order.ReceivedAtUtc,
        order.PromisedAtUtc,
        order.DeliveredAtUtc,
        order.Device!.Brand,
        order.Device.Model,
        order.Device.Customer!.FullName,
        order.Technician != null ? order.Technician.FullName : null);

    /// <summary>Órdenes que siguen en el taller: ni entregadas ni canceladas.</summary>
    public static IQueryable<ServiceOrder> WhereOpen(this IQueryable<ServiceOrder> orders) =>
        orders.Where(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled);
}
