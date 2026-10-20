using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
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
            var technician = new Technician("Luis Pérez", "Laptops", null, null, Now);
            context.Customers.Add(customer);
            context.Technicians.Add(technician);
            await context.SaveChangesAsync(Token);

            var device = customer.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", "SN-001", null, Now);
            await context.SaveChangesAsync(Token);

            var order = new ServiceOrder(device, "No enciende", ServiceOrderPriority.High, Now, Now.AddDays(2), 350.50m, "Con cargador", technician);
            order.ChangeStatus(ServiceOrderStatus.Diagnosing, Now.AddHours(1));
            context.ServiceOrders.Add(order);
            await context.SaveChangesAsync(Token);
        }

        await using var reader = _database.CreateContext();
        var saved = await reader.ServiceOrders.Include(o => o.StatusHistory).SingleAsync(Token);

        Assert.Equal(DateTimeKind.Utc, saved.ReceivedAtUtc.Kind);
        Assert.Equal(Now, saved.ReceivedAtUtc);
        Assert.Equal(350.50m, saved.EstimatedCost);
        Assert.Equal("Con cargador", saved.IntakeNotes);
        Assert.Equal(Now, saved.TechnicianAssignedAtUtc);
        Assert.Equal(1, saved.Version);
        Assert.Equal(2, saved.StatusHistory.Count);
        Assert.All(saved.StatusHistory, entry => Assert.Equal(DateTimeKind.Utc, entry.ChangedAtUtc.Kind));
    }

    [Fact]
    public async Task A_device_cannot_reference_a_missing_customer()
    {
        // El dominio no permite crear un equipo sin propietario; se prueba la clave foránea con SQL directo.
        await using var context = _database.CreateContext();

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Devices (CustomerId, Type, Brand, CreatedAtUtc) VALUES (999, 1, 'HP', '2026-10-19 15:00:00')",
            Token));
    }

    [Fact]
    public async Task A_customer_with_devices_cannot_be_deleted()
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(Token);
        customer.RegisterDevice(DeviceType.Tablet, "Apple", "iPad", null, null, Now);
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
    public async Task Serial_numbers_are_unique_within_a_brand()
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(Token);
        customer.RegisterDevice(DeviceType.Laptop, "Dell", null, "SN-1", null, Now);
        customer.RegisterDevice(DeviceType.Printer, "HP", null, "SN-1", null, Now);
        customer.RegisterDevice(DeviceType.Monitor, "Dell", null, null, null, Now);
        customer.RegisterDevice(DeviceType.Monitor, "Dell", null, null, null, Now);
        await context.SaveChangesAsync(Token);

        customer.RegisterDevice(DeviceType.Desktop, "Dell", null, "SN-1", null, Now);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Theory]
    [InlineData("UPDATE Devices SET SerialNumber = ''")]
    [InlineData("UPDATE Technicians SET Specialty = ''")]
    [InlineData("UPDATE Technicians SET IsActive = 0")]
    public async Task The_database_rejects_empty_values_and_inconsistent_states(string sql)
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        context.Technicians.Add(new Technician("Luis Pérez", "Laptops", null, null, Now));
        await context.SaveChangesAsync(Token);
        customer.RegisterDevice(DeviceType.Laptop, "Dell", null, "SN-1", null, Now);
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(sql, Token));
    }

    [Fact]
    public async Task Deactivating_a_customer_keeps_its_devices_and_their_history()
    {
        int customerId;
        await using (var context = _database.CreateContext())
        {
            var customer = new Customer("Ana López", "5555-1234", null, null, Now);
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Token);
            var device = customer.RegisterDevice(DeviceType.Laptop, "Dell", null, null, null, Now);
            await context.SaveChangesAsync(Token);
            context.ServiceOrders.Add(new ServiceOrder(device, "No enciende", ServiceOrderPriority.Normal, Now, null, null, null));
            customer.Deactivate(Now.AddDays(1));
            await context.SaveChangesAsync(Token);
            customerId = customer.Id;
        }

        await using var reader = _database.CreateContext();
        Assert.False(reader.Customers.Single(c => c.Id == customerId).IsActive);
        Assert.Equal(1, reader.Devices.Count(d => d.CustomerId == customerId));
        Assert.Equal(1, reader.ServiceOrderStatusHistory.Count());
    }

    [Fact]
    public async Task The_database_rejects_out_of_range_statuses_and_negative_amounts()
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        context.Customers.Add(customer);
        await context.SaveChangesAsync(Token);
        var device = customer.RegisterDevice(DeviceType.Laptop, "Lenovo", null, null, null, Now);
        await context.SaveChangesAsync(Token);
        context.ServiceOrders.Add(new ServiceOrder(device, "Pantalla rota", ServiceOrderPriority.Normal, Now, null, null, null));
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE ServiceOrders SET Status = 9", Token));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE ServiceOrders SET EstimatedCost = -1", Token));
    }

    [Theory]
    [InlineData("UPDATE ServiceOrders SET FinalCost = 10")]
    [InlineData("UPDATE ServiceOrders SET TechnicianAssignedAtUtc = NULL")]
    [InlineData("UPDATE ServiceOrders SET Status = 6, DeliveredAtUtc = '2026-10-20 10:00:00'")]
    public async Task The_database_rejects_inconsistent_assignments_and_final_costs(string sql)
    {
        await using var context = _database.CreateContext();
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        var technician = new Technician("Luis Pérez", "Laptops", null, null, Now);
        context.AddRange(customer, technician);
        await context.SaveChangesAsync(Token);
        var device = customer.RegisterDevice(DeviceType.Laptop, "Lenovo", null, null, null, Now);
        context.ServiceOrders.Add(new ServiceOrder(device, "Pantalla rota", ServiceOrderPriority.Normal, Now, null, null, null, technician));
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(sql, Token));
    }

    [Fact]
    public async Task Concurrent_edits_of_customers_devices_and_technicians_are_detected()
    {
        int customerId, deviceId, technicianId;
        await using (var setup = _database.CreateContext())
        {
            var customer = new Customer("Ana López", "5555-1234", null, null, Now);
            var technician = new Technician("Luis Pérez", "Laptops", null, null, Now);
            setup.AddRange(customer, technician);
            await setup.SaveChangesAsync(Token);
            var device = customer.RegisterDevice(DeviceType.Laptop, "Lenovo", null, null, null, Now);
            await setup.SaveChangesAsync(Token);
            (customerId, deviceId, technicianId) = (customer.Id, device.Id, technician.Id);
        }

        // Dos personas leen la misma versión y guardan por separado: la segunda no sobrescribe a la primera.
        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        (await first.Customers.SingleAsync(c => c.Id == customerId, Token)).UpdateDetails("Ana María López", "5555-1234", null, null, Now.AddHours(1));
        (await first.Devices.SingleAsync(d => d.Id == deviceId, Token)).UpdateDetails(DeviceType.Laptop, "Lenovo", "T14", null, null, Now.AddHours(1));
        (await first.Technicians.SingleAsync(t => t.Id == technicianId, Token)).UpdateDetails("Luis A. Pérez", "Laptops", null, null, Now.AddHours(1));
        var staleCustomer = await second.Customers.SingleAsync(c => c.Id == customerId, Token);
        var staleDevice = await second.Devices.SingleAsync(d => d.Id == deviceId, Token);
        var staleTechnician = await second.Technicians.SingleAsync(t => t.Id == technicianId, Token);
        await first.SaveChangesAsync(Token);

        staleCustomer.UpdateDetails("Ana Desactualizada", "5555-0000", null, null, Now.AddHours(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
        second.ChangeTracker.Clear();
        second.Attach(staleDevice).Entity.UpdateDetails(DeviceType.Laptop, "Lenovo", "Viejo", null, null, Now.AddHours(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
        second.ChangeTracker.Clear();
        second.Attach(staleTechnician).Entity.UpdateDetails("Luis Desactualizado", "Laptops", null, null, Now.AddHours(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        await using var reader = _database.CreateContext();
        Assert.Equal(("Ana María López", 1), await reader.Customers.Where(c => c.Id == customerId).Select(c => ValueTuple.Create(c.FullName, c.Version)).SingleAsync(Token));
        Assert.Equal("T14", await reader.Devices.Where(d => d.Id == deviceId).Select(d => d.Model).SingleAsync(Token));
        Assert.Equal("Luis A. Pérez", await reader.Technicians.Where(t => t.Id == technicianId).Select(t => t.FullName).SingleAsync(Token));
    }

    [Fact]
    public async Task Concurrent_changes_to_the_same_order_are_detected()
    {
        int orderId;
        await using (var setup = _database.CreateContext())
        {
            var customer = new Customer("Ana López", "5555-1234", null, null, Now);
            setup.Customers.Add(customer);
            await setup.SaveChangesAsync(Token);
            var device = customer.RegisterDevice(DeviceType.Laptop, "Lenovo", null, null, null, Now);
            var order = new ServiceOrder(device, "Pantalla rota", ServiceOrderPriority.Normal, Now, null, null, null);
            setup.ServiceOrders.Add(order);
            await setup.SaveChangesAsync(Token);
            orderId = order.Id;
        }

        // Dos usuarios leen la misma versión de la orden y la modifican por separado.
        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var firstCopy = await first.ServiceOrders.SingleAsync(o => o.Id == orderId, Token);
        var secondCopy = await second.ServiceOrders.SingleAsync(o => o.Id == orderId, Token);

        firstCopy.UpdatePlan(ServiceOrderPriority.Urgent, null, 100m, Now.AddHours(1));
        await first.SaveChangesAsync(Token);
        secondCopy.UpdatePlan(ServiceOrderPriority.Low, null, 50m, Now.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
        await using var reader = _database.CreateContext();
        var saved = await reader.ServiceOrders.SingleAsync(o => o.Id == orderId, Token);
        Assert.Equal(ServiceOrderPriority.Urgent, saved.Priority);
        Assert.Equal(1, saved.Version);
    }
}

