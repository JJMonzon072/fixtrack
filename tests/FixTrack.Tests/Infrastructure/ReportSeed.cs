using FixTrack.Application.Reports;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Infrastructure.Persistence;

namespace FixTrack.Tests.Infrastructure;

/// <summary>
/// Datos ficticios con fechas exactas para las pruebas de reportes, interpretadas en America/Guatemala (UTC−6, sin
/// horario de verano). El período de referencia es octubre de 2026: del 1 de octubre 00:00 local (06:00 UTC) al
/// 1 de noviembre 00:00 local, excluido.
/// <code>
/// A  recibida 01-oct 00:00 local (límite inicial)  · Alta    · Carla · entregada 04-oct a tiempo · Q400 → Q450
/// B  recibida 30-sep (fuera)                       · Normal  · Luis  · entregada 03-oct, sin fecha · sin estimado → Q200
/// C  recibida 31-oct 23:59 local (último minuto)   · Urgente · sin técnico · Recibida · Q100 estimado
/// D  recibida 01-nov 00:00 local (límite final)    · Baja    · sin técnico · Recibida (fuera)
/// E  recibida 10-oct                               · Normal  · Carla · entregada 15-oct con atraso · Q300 → Q250
/// F  recibida 12-oct                               · Baja    · Luis  · cancelada 13-oct
/// G  recibida 20-sep (fuera)                       · Normal  · Luis  · cancelada 02-oct
/// H  recibida 20-oct                               · Alta    · Carla · cancelada 02-nov (fuera)
/// I  recibida 25-oct                               · Urgente · Carla · En reparación (abierta)
/// J  recibida 01-sep, entregada 10-sep (todo fuera) · Normal · Luis  · Q90
/// K  recibida 05-oct, lista, reabierta, lista otra vez · Normal · Luis · entregada 12-oct a tiempo · Q500 → Q520
/// </code>
/// Totales de octubre: 7 recibidas (A C E F H I K), 4 entregadas (A B E K), 2 canceladas (F G), 1 entrega con atraso
/// de 3 con fecha, ingresos registrados Q1 420.00, 111 h promedio hasta la entrega y 91.5 h hasta quedar lista.
/// </summary>
internal static class ReportSeed
{
    public static readonly TimeZoneInfo Guatemala = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");

    public static ReportPeriod October => ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Guatemala);

    /// <summary>
    /// Carga el escenario; con <paramref name="copies"/> mayor que 1 lo repite con otros clientes y técnicos, numerados
    /// desde <paramref name="firstCopy"/> (la copia 1 usa los nombres sin número).
    /// </summary>
    public static async Task SeedAsync(Func<FixTrackDbContext> createContext, CancellationToken token, int copies = 1, int firstCopy = 1)
    {
        for (var copy = firstCopy; copy < firstCopy + copies; copy++)
        {
            await SeedCopyAsync(createContext, copy, token);
        }
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task SeedCopyAsync(Func<FixTrackDbContext> createContext, int copy, CancellationToken token)
    {
        var suffix = copy == 1 ? string.Empty : $" {copy}";
        var created = Utc(8, 1, 15);
        await using var context = createContext();
        var ana = new Customer($"Ana Morales{suffix}", "5555-0101", $"ana{copy}@fixtrack.test", null, created);
        var carla = new Technician($"Carla Ruiz{suffix}", "Laptops", "5555-0201", $"carla{copy}@fixtrack.test", created);
        var luis = new Technician($"Luis Pérez{suffix}", "Impresoras", null, null, created);
        context.AddRange(ana, carla, luis);
        await context.SaveChangesAsync(token);

        var dell = ana.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", $"RPT-DELL-{copy}", null, created);
        var hp = ana.RegisterDevice(DeviceType.Printer, "HP", "LaserJet", $"RPT-HP-{copy}", null, created);
        await context.SaveChangesAsync(token);

        var a = new ServiceOrder(dell, "No enciende", ServiceOrderPriority.High, Utc(10, 1, 6), Utc(10, 5, 18), 400m, "Con cargador", carla);
        a.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(10, 2, 6));
        a.RecordDiagnosis("Fuente dañada", Utc(10, 2, 7));
        a.ChangeStatus(ServiceOrderStatus.InRepair, Utc(10, 2, 8));
        a.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(10, 4, 12));
        a.ChangeStatus(ServiceOrderStatus.Delivered, Utc(10, 4, 18), null, 450m);

        var b = new ServiceOrder(hp, "Atasco de papel", ServiceOrderPriority.Normal, Utc(9, 30, 12), null, null, null, luis);
        b.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(10, 1, 12));
        b.RecordDiagnosis("Rodillo gastado", Utc(10, 1, 13));
        b.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(10, 2, 12));
        b.ChangeStatus(ServiceOrderStatus.Delivered, Utc(10, 3, 12), null, 200m);

        var c = new ServiceOrder(dell, "Pantalla con líneas", ServiceOrderPriority.Urgent, Utc(11, 1, 5, 59), null, 100m, null);
        var d = new ServiceOrder(hp, "Mantenimiento", ServiceOrderPriority.Low, Utc(11, 1, 6), null, null, null);

        var e = new ServiceOrder(dell, "Teclado", ServiceOrderPriority.Normal, Utc(10, 10, 15), Utc(10, 12, 15), 300m, null, carla);
        e.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(10, 10, 16));
        e.RecordDiagnosis("Teclado con derrame", Utc(10, 10, 17));
        e.ChangeStatus(ServiceOrderStatus.InRepair, Utc(10, 11, 15));
        e.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(10, 14, 15));
        e.ChangeStatus(ServiceOrderStatus.Delivered, Utc(10, 15, 15), null, 250m);

        var f = new ServiceOrder(hp, "No imprime", ServiceOrderPriority.Low, Utc(10, 12, 15), null, null, null, luis);
        f.ChangeStatus(ServiceOrderStatus.Cancelled, Utc(10, 13, 15), "El cliente retiró el equipo");

        var g = new ServiceOrder(hp, "Ruido", ServiceOrderPriority.Normal, Utc(9, 20, 15), null, null, null, luis);
        g.ChangeStatus(ServiceOrderStatus.Cancelled, Utc(10, 2, 15), "Duplicada");

        var h = new ServiceOrder(dell, "Bisagra", ServiceOrderPriority.High, Utc(10, 20, 15), null, null, null, carla);
        h.ChangeStatus(ServiceOrderStatus.Cancelled, Utc(11, 2, 15), "Sin repuesto");

        var i = new ServiceOrder(dell, "Batería", ServiceOrderPriority.Urgent, Utc(10, 25, 15), null, null, null, carla);
        i.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(10, 25, 16));
        i.RecordDiagnosis("Batería inflada", Utc(10, 25, 17));
        i.ChangeStatus(ServiceOrderStatus.InRepair, Utc(10, 26, 15));

        var j = new ServiceOrder(hp, "Tóner", ServiceOrderPriority.Normal, Utc(9, 1, 15), null, null, null, luis);
        j.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(9, 1, 16));
        j.RecordDiagnosis("Tóner vacío", Utc(9, 1, 17));
        j.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(9, 2, 15));
        j.ChangeStatus(ServiceOrderStatus.Delivered, Utc(9, 10, 15), null, 90m);

        var k = new ServiceOrder(hp, "Escáner", ServiceOrderPriority.Normal, Utc(10, 5, 15), Utc(10, 20, 15), 500m, null, luis);
        k.ChangeStatus(ServiceOrderStatus.Diagnosing, Utc(10, 5, 16));
        k.RecordDiagnosis("Sensor sucio", Utc(10, 5, 17));
        k.ChangeStatus(ServiceOrderStatus.InRepair, Utc(10, 6, 15));
        k.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(10, 8, 15));
        k.ChangeStatus(ServiceOrderStatus.InRepair, Utc(10, 9, 15), "El cliente reportó otra falla");
        k.ChangeStatus(ServiceOrderStatus.ReadyForPickup, Utc(10, 11, 15));
        k.ChangeStatus(ServiceOrderStatus.Delivered, Utc(10, 12, 15), null, 520m);

        foreach (var order in new[] { a, b, c, d, e, f, g, h, i, j, k })
        {
            context.ServiceOrders.Add(order);
            await context.SaveChangesAsync(token);
        }
    }
}
