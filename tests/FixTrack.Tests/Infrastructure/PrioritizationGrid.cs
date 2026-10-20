using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

/// <summary>
/// Órdenes abiertas en todos los bordes de la fórmula de priorización: cada prioridad, fechas comprometidas vencidas,
/// exactamente ahora, justo antes y después de 24 y 72 horas, y recepciones justo antes y después de días completos y
/// del tope de antigüedad. Muchas empatan en puntuación, así que también se prueban los desempates.
/// </summary>
internal static class PrioritizationGrid
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    private static readonly TimeSpan?[] DueOffsets =
    [
        null,
        TimeSpan.FromDays(-1),
        -Millisecond,
        TimeSpan.Zero,
        Millisecond,
        TimeSpan.FromHours(24) - Millisecond,
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(24) + Millisecond,
        TimeSpan.FromHours(72),
        TimeSpan.FromHours(72) + Millisecond,
        TimeSpan.FromDays(10),
    ];

    private static readonly TimeSpan[] ReceivedOffsets =
    [
        TimeSpan.FromHours(-1),
        TimeSpan.FromDays(-1) + Millisecond,
        TimeSpan.FromDays(-1),
        TimeSpan.FromDays(-1) - Millisecond,
        TimeSpan.FromDays(-14),
        TimeSpan.FromDays(-15),
        TimeSpan.FromDays(-15) - Millisecond,
        TimeSpan.FromDays(-40),
    ];

    /// <summary>Carga la rejilla. <paramref name="nowUtc"/> debe estar truncado a milisegundos.</summary>
    public static async Task<int> SeedAsync(Func<FixTrackDbContext> createContext, DateTime nowUtc, CancellationToken token)
    {
        await using var context = createContext();
        var customer = new Customer("Cliente Rejilla", "5555-0900", null, null, nowUtc.AddDays(-400));
        context.Customers.Add(customer);
        await context.SaveChangesAsync(token);
        var device = customer.RegisterDevice(DeviceType.Laptop, "Rejilla", "Prioridades", null, null, nowUtc.AddDays(-400));
        await context.SaveChangesAsync(token);

        var count = 0;
        foreach (var priority in Enum.GetValues<ServiceOrderPriority>())
        {
            foreach (var receivedOffset in ReceivedOffsets)
            {
                foreach (var dueOffset in DueOffsets)
                {
                    var received = nowUtc + receivedOffset;
                    var promised = dueOffset is { } offset ? nowUtc + offset : (DateTime?)null;
                    if (promised < received)
                    {
                        continue;
                    }

                    context.ServiceOrders.Add(new ServiceOrder(device, "Rejilla de prioridades", priority, received, promised, null, null));
                    count++;
                }
            }
        }

        await context.SaveChangesAsync(token);
        return count;
    }

    /// <summary>Orden esperado: el de <see cref="WorkPrioritizer"/> sobre las órdenes abiertas, calculado en memoria.</summary>
    public static List<int> ExpectedOrder(IEnumerable<OpenOrderSnapshot> orders, DateTime nowUtc) =>
    [
        .. WorkPrioritizer
            .Rank(
                orders,
                order => WorkPrioritizer.Score(order.Priority, order.Status, order.ReceivedAtUtc, order.PromisedAtUtc, order.TechnicianId is not null, nowUtc).Total,
                order => order.PromisedAtUtc,
                order => order.ReceivedAtUtc,
                order => order.Id)
            .Select(order => order.Id),
    ];

    /// <summary>Todas las órdenes abiertas tal como las devuelve la base, ordenadas por la puntuación calculada en SQL.</summary>
    public static Task<IReadOnlyList<OpenOrderSnapshot>> RankedByDatabaseAsync(FixTrackDbContext context, DateTime nowUtc, int take, CancellationToken token) =>
        new ServiceOrderRepository(context).ListMostUrgentAsync(ServiceOrderWorkflow.OpenStatuses, nowUtc, take, token);
}
