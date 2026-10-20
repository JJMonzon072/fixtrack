using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Restricciones y relaciones del modelo, verificadas contra una base relacional real (SQLite en memoria).</summary>
public sealed class PersistenceModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Saves_a_full_service_order_graph_and_reads_dates_back_as_utc()
    {
        await using (var context = _database.CreateContext())
        {
            var customer = new Customer("Ana López", "5555-1234", "ana@example.com", null, Now);
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Token);

            var device = new Device(customer.Id, DeviceType.Laptop, "Dell", "Latitude 5440", "SN-001", null, Now);
            context.Devices.Add(device);
            await context.SaveChangesAsync(Token);

            var order = new ServiceOrder(device.Id, "No enciende", ServiceOrderPriority.High, Now, Now.AddDays(2), 350.50m);
            order.ChangeStatus(ServiceOrderStatus.Diagnosing, Now.AddHours(1));
            context.ServiceOrders.Add(order);
            await context.SaveChangesAsync(Token);
        }

        await using var reader = _database.CreateContext();
        var saved = await reader.ServiceOrders.Include(o => o.StatusHistory).SingleAsync(Token);

        Assert.Equal(DateTimeKind.Utc, saved.ReceivedAtUtc.Kind);
        Assert.Equal(Now, saved.ReceivedAtUtc);
        Assert.Equal(350.50m, saved.EstimatedCost);
        Assert.Equal(2, saved.StatusHistory.Count);
        Assert.All(saved.StatusHistory, entry => Assert.Equal(DateTimeKind.Utc, entry.ChangedAtUtc.Kind));
    }

    [Fact]
    public async Task A_device_cannot_reference_a_missing_customer()
    {
        await using var context = _database.CreateContext();
        context.Devices.Add(new Device(customerId: 999, DeviceType.Desktop, "HP", null, null, null, Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task A_customer_with_devices_cannot_be_deleted()
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(Token);
        context.Devices.Add(new Device(customer.Id, DeviceType.Tablet, "Apple", "iPad", null, null, Now));
        await context.SaveChangesAsync(Token);

        // Simula un borrado directo en la base: la clave foránea lo impide (ON DELETE NO ACTION).
        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM Customers", Token));
    }

    [Fact]
    public async Task Customer_emails_are_unique_but_optional()
    {
        await using var context = _database.CreateContext();
        context.Customers.Add(new Customer("Ana López", "5555-1234", null, null, Now));
        context.Customers.Add(new Customer("Luis Pérez", "5555-5678", null, null, Now));
        await context.SaveChangesAsync(Token);

        context.Customers.Add(new Customer("Carla Ruiz", "5555-0001", "carla@example.com", null, Now));
        await context.SaveChangesAsync(Token);
        context.Customers.Add(new Customer("Carla Duplicada", "5555-0002", "carla@example.com", null, Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task The_database_rejects_an_inconsistent_deactivation()
    {
        await using var context = _database.CreateContext();
        context.Customers.Add(new Customer("Ana López", "5555-1234", null, null, Now));
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE Customers SET IsActive = 0", Token));
    }

    [Fact]
    public async Task The_database_rejects_out_of_range_statuses_and_negative_amounts()
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(Token);
        var device = new Device(customer.Id, DeviceType.Laptop, "Lenovo", null, null, null, Now);
        context.Devices.Add(device);
        await context.SaveChangesAsync(Token);
        context.ServiceOrders.Add(new ServiceOrder(device.Id, "Pantalla rota", ServiceOrderPriority.Normal, Now, null, null));
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE ServiceOrders SET Status = 9", Token));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE ServiceOrders SET EstimatedCost = -1", Token));
    }
}
