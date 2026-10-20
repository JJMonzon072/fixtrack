using FixTrack.Application.Common;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Transacciones, bloqueo del técnico y traducción de errores de concurrencia (SQLite en memoria).</summary>
public sealed class UnitOfWorkTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_successful_operation_is_committed()
    {
        await using (var context = _database.CreateContext())
        {
            var result = await new EfUnitOfWork(context).ExecuteInTransactionAsync(
                async token =>
                {
                    context.Technicians.Add(new Technician("Luis Pérez", "Laptops", null, null, Now));
                    await context.SaveChangesAsync(token);
                    return CommandResult.Success();
                },
                Token);

            Assert.True(result.Succeeded);
        }

        await using var reader = _database.CreateContext();
        Assert.Equal(1, await reader.Technicians.CountAsync(Token));
    }

    [Fact]
    public async Task An_unsuccessful_operation_is_rolled_back_even_after_saving()
    {
        await using (var context = _database.CreateContext())
        {
            var result = await new EfUnitOfWork(context).ExecuteInTransactionAsync(
                async token =>
                {
                    context.Technicians.Add(new Technician("Luis Pérez", "Laptops", null, null, Now));
                    await context.SaveChangesAsync(token);
                    return CommandResult.Conflict("Algo impidió completar la operación.");
                },
                Token);

            Assert.Equal(CommandStatus.Conflict, result.Status);
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var reader = _database.CreateContext();
        Assert.Equal(0, await reader.Technicians.CountAsync(Token));
    }

    [Fact]
    public async Task An_exception_rolls_the_transaction_back()
    {
        await using (var context = _database.CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new EfUnitOfWork(context).ExecuteInTransactionAsync<CommandResult>(
                async token =>
                {
                    context.Technicians.Add(new Technician("Luis Pérez", "Laptops", null, null, Now));
                    await context.SaveChangesAsync(token);
                    throw new InvalidOperationException("Falla simulada");
                },
                Token));
        }

        await using var reader = _database.CreateContext();
        Assert.Equal(0, await reader.Technicians.CountAsync(Token));
    }

    [Fact]
    public async Task Concurrent_order_changes_are_reported_as_a_conflict()
    {
        await using (var setup = _database.CreateContext())
        {
            var customer = new Customer("Ana López", "5555-1234", null, null, Now);
            setup.Customers.Add(customer);
            await setup.SaveChangesAsync(Token);
            var device = customer.RegisterDevice(DeviceType.Laptop, "Dell", null, null, null, Now);
            setup.ServiceOrders.Add(new ServiceOrder(device, "No enciende", ServiceOrderPriority.Normal, Now, null, null, null));
            await setup.SaveChangesAsync(Token);
        }

        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var firstCopy = await first.ServiceOrders.SingleAsync(Token);
        var secondCopy = await second.ServiceOrders.SingleAsync(Token);
        firstCopy.UpdatePlan(ServiceOrderPriority.Urgent, null, null, Now.AddHours(1));
        secondCopy.UpdatePlan(ServiceOrderPriority.Low, null, null, Now.AddHours(1));
        await new EfUnitOfWork(first).SaveChangesAsync(Token);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(second).SaveChangesAsync(Token));
    }
}
