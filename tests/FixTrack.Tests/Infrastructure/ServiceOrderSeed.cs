using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Órdenes de ejemplo compartidas por las pruebas de consultas (SQLite en memoria).</summary>
internal static class ServiceOrderSeed
{
    /// <summary>
    /// Cinco órdenes, insertadas en este orden (Id 1 a 5):
    /// 1 Ana · Dell · Alta · Carla · En reparación · vencida;
    /// 2 Mario · HP · Normal · sin técnico · Recibida · entrega mañana;
    /// 3 Ana · Dell · Baja · Carla · Entregada;
    /// 4 Mario · HP · Urgente · Luis · Cancelada;
    /// 5 Ana · Lenovo · Urgente · Luis · En diagnóstico · entrega en 5 días.
    /// </summary>
    public static async Task SeedAsync(SqliteTestDatabase database, DateTime now, CancellationToken token)
    {
        await using var context = database.CreateContext();
        var ana = new Customer("Ana Morales", "5555-0001", null, null, now.AddDays(-60));
        var mario = new Customer("Mario Castillo", "5555-0002", null, null, now.AddDays(-60));
        var carla = new Technician("Carla Ruiz", "Laptops", null, null, now.AddDays(-90));
        var luis = new Technician("Luis Pérez", "Impresoras", null, null, now.AddDays(-90));
        context.AddRange(ana, mario, carla, luis);
        await context.SaveChangesAsync(token);

        var dell = ana.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", "SN-DELL-1", null, now.AddDays(-60));
        var lenovo = ana.RegisterDevice(DeviceType.Desktop, "Lenovo", "ThinkCentre", null, null, now.AddDays(-60));
        var hp = mario.RegisterDevice(DeviceType.Printer, "HP", "LaserJet", "SN-HP-9", null, now.AddDays(-60));
        await context.SaveChangesAsync(token);

        var inRepair = new ServiceOrder(dell, "No enciende", ServiceOrderPriority.High, now.AddDays(-5), now.AddDays(-1), 400m, null, carla);
        inRepair.ChangeStatus(ServiceOrderStatus.Diagnosing, now.AddDays(-4));
        inRepair.RecordDiagnosis("Fuente dañada", now.AddDays(-4));
        inRepair.ChangeStatus(ServiceOrderStatus.InRepair, now.AddDays(-3));

        var received = new ServiceOrder(hp, "Atasco de papel", ServiceOrderPriority.Normal, now.AddDays(-2), now.AddDays(1), null, null);

        var delivered = new ServiceOrder(dell, "Teclado", ServiceOrderPriority.Low, now.AddDays(-10), now.AddDays(-8), 150m, null, carla);
        delivered.ChangeStatus(ServiceOrderStatus.Diagnosing, now.AddDays(-10));
        delivered.RecordDiagnosis("Teclado con derrame", now.AddDays(-10));
        delivered.ChangeStatus(ServiceOrderStatus.ReadyForPickup, now.AddDays(-9));
        delivered.ChangeStatus(ServiceOrderStatus.Delivered, now.AddDays(-7), "Pagado", 175m);

        var cancelled = new ServiceOrder(hp, "No imprime", ServiceOrderPriority.Urgent, now.AddDays(-1), null, null, null, luis);
        cancelled.ChangeStatus(ServiceOrderStatus.Cancelled, now.AddHours(-20), "Duplicada");

        var diagnosing = new ServiceOrder(lenovo, "Lento", ServiceOrderPriority.Urgent, now.AddHours(-3), now.AddDays(5), null, null, luis);
        diagnosing.ChangeStatus(ServiceOrderStatus.Diagnosing, now.AddHours(-2));

        foreach (var order in new[] { inRepair, received, delivered, cancelled, diagnosing })
        {
            context.ServiceOrders.Add(order);
            await context.SaveChangesAsync(token);
        }
    }
}
