using System.Linq.Expressions;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
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

    /// <summary>Elemento del listado de órdenes con su cliente, equipo y técnico (una sola consulta con JOIN).</summary>
    public static readonly Expression<Func<ServiceOrder, ServiceOrderListItem>> ListItem = order => new ServiceOrderListItem(
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
        order.DeliveredAtUtc);

    /// <summary>
    /// Puntuación del algoritmo de priorización calculada por la base, idéntica a <see cref="WorkPrioritizer.Score"/>
    /// para el mismo instante <paramref name="nowUtc"/> (truncado a milisegundos, la precisión de las columnas). Cada
    /// término se reduce a comparaciones con umbrales calculados aquí: "días completos desde la recepción ≥ k" equivale a
    /// "recibida ≤ ahora − k días". No se usa DATEDIFF, que cuenta cambios de fecha y no días completos. Los umbrales
    /// viajan como parámetros: la consulta se compila una sola vez.
    /// </summary>
    public static Expression<Func<ServiceOrder, int>> PriorityScore(DateTime nowUtc)
    {
        var thresholds = Expression.Constant(new ScoreThresholds(nowUtc));
        var order = Expression.Parameter(typeof(ServiceOrder), "order");
        Expression Threshold(string name) => Expression.Property(thresholds, name);
        static Expression Points(int value) => Expression.Constant(value);

        var priority = Expression.Property(order, nameof(ServiceOrder.Priority));
        var priorityPoints = Expression.Condition(
            Expression.Equal(priority, Expression.Constant(ServiceOrderPriority.Low)),
            Points(WorkPrioritizer.LowPriorityPoints),
            Expression.Condition(
                Expression.Equal(priority, Expression.Constant(ServiceOrderPriority.Normal)),
                Points(WorkPrioritizer.NormalPriorityPoints),
                Expression.Condition(
                    Expression.Equal(priority, Expression.Constant(ServiceOrderPriority.High)),
                    Points(WorkPrioritizer.HighPriorityPoints),
                    Points(WorkPrioritizer.UrgentPriorityPoints))));

        // Bonos de fecha excluyentes: vencida, 24 horas o menos, 72 horas o menos.
        var promised = Expression.Property(order, nameof(ServiceOrder.PromisedAtUtc));
        Expression Before(string name) => Expression.LessThan(promised, Expression.Convert(Threshold(name), typeof(DateTime?)));
        Expression AtMost(string name) => Expression.LessThanOrEqual(promised, Expression.Convert(Threshold(name), typeof(DateTime?)));
        var duePoints = Expression.Condition(
            Expression.Equal(promised, Expression.Constant(null, typeof(DateTime?))),
            Points(0),
            Expression.Condition(
                Before(nameof(ScoreThresholds.Now)),
                Points(WorkPrioritizer.OverduePoints),
                Expression.Condition(
                    AtMost(nameof(ScoreThresholds.NearDue)),
                    Points(WorkPrioritizer.DueWithin24HoursPoints),
                    Expression.Condition(AtMost(nameof(ScoreThresholds.SoonDue)), Points(WorkPrioritizer.DueWithin72HoursPoints), Points(0)))));

        // Antigüedad: +2 por día completo hasta el tope. La condición más externa es la de más días.
        var received = Expression.Property(order, nameof(ServiceOrder.ReceivedAtUtc));
        Expression agePoints = Points(0);
        for (var day = 1; day <= ScoreThresholds.AgeSteps; day++)
        {
            var receivedBefore = Expression.ArrayIndex(Threshold(nameof(ScoreThresholds.DaysAgo)), Expression.Constant(day));
            agePoints = Expression.Condition(
                Expression.LessThanOrEqual(received, receivedBefore),
                Points(day * WorkPrioritizer.PointsPerDayOfAge),
                agePoints);
        }

        return Expression.Lambda<Func<ServiceOrder, int>>(
            Expression.Add(Expression.Add(priorityPoints, duePoints), agePoints),
            order);
    }

    /// <summary>Umbrales de la puntuación para un instante: la consulta los recibe como parámetros.</summary>
    private sealed class ScoreThresholds(DateTime nowUtc)
    {
        public static readonly int AgeSteps = WorkPrioritizer.MaxAgePoints / WorkPrioritizer.PointsPerDayOfAge;

        public DateTime Now { get; } = nowUtc;

        public DateTime NearDue { get; } = nowUtc + WorkPrioritizer.NearDueWindow;

        public DateTime SoonDue { get; } = nowUtc + WorkPrioritizer.SoonDueWindow;

        /// <summary>Posición k: el instante de hace k días completos (la posición 0 no se usa).</summary>
        public DateTime[] DaysAgo { get; } = [.. Enumerable.Range(0, AgeSteps + 1).Select(day => nowUtc.AddDays(-day))];
    }

    /// <summary>Órdenes que siguen en el taller: ni entregadas ni canceladas.</summary>
    public static IQueryable<ServiceOrder> WhereOpen(this IQueryable<ServiceOrder> orders) =>
        orders.Where(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled);
}
