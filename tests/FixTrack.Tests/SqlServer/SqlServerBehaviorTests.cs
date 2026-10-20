using System.Diagnostics;
using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// Comportamientos que SQLite no reproduce y se comprueban contra SQL Server: el trigger del historial, los
/// bloqueos UPDLOCK, la concurrencia optimista y la intercalación sin tildes. Solo se ejecutan si la variable de
/// entorno <c>FIXTRACK_TEST_SQLSERVER</c> contiene una cadena de conexión; en GitHub Actions se omiten.
/// Cada prueba trabaja dentro de transacciones que se revierten, de modo que no deja cambios en la base.
/// </summary>
public sealed class SqlServerBehaviorTests
{
    private const string ConnectionVariable = "FIXTRACK_TEST_SQLSERVER";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string ConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), $"Prueba de SQL Server omitida: define {ConnectionVariable} para ejecutarla.");
        return value!;
    }

    private static FixTrackDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connectionString).Options);

    private static FixTrackDbContext CreateContext(SqlConnection connection) =>
        new(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connection).Options);

    [Fact]
    public async Task The_status_history_rejects_updates()
    {
        await using var context = CreateContext(ConnectionString());
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var entryId = await context.ServiceOrderStatusHistory.Select(entry => entry.Id).FirstOrDefaultAsync(Token);
        Assert.SkipWhen(entryId == 0, "No hay registros de historial para probar el trigger.");

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlAsync($"UPDATE [ServiceOrderStatusHistory] SET [Note] = N'modificada' WHERE [Id] = {entryId}", Token));
        Assert.Equal(50001, update.Number);

        // El error del trigger ya revirtió la transacción en el servidor; al liberarla no queda nada pendiente.
    }

    [Fact]
    public async Task The_status_history_rejects_deletes()
    {
        await using var context = CreateContext(ConnectionString());
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var entryId = await context.ServiceOrderStatusHistory.Select(entry => entry.Id).FirstOrDefaultAsync(Token);
        Assert.SkipWhen(entryId == 0, "No hay registros de historial para probar el trigger.");

        var delete = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlAsync($"DELETE FROM [ServiceOrderStatusHistory] WHERE [Id] = {entryId}", Token));
        Assert.Equal(50001, delete.Number);

        // El error del trigger ya revirtió la transacción en el servidor; al liberarla no queda nada pendiente.
    }

    [Fact]
    public async Task The_technician_lock_makes_a_second_locker_wait_but_not_plain_readers()
    {
        var connectionString = ConnectionString();
        await using var first = CreateContext(connectionString);
        await using var second = CreateContext(connectionString);
        var technicianId = await first.Technicians.Select(technician => technician.Id).FirstOrDefaultAsync(Token);
        Assert.SkipWhen(technicianId == 0, "No hay técnicos para probar el bloqueo.");

        await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
        Assert.NotNull(await new TechnicianRepository(first).FindForAssignmentAsync(technicianId, Token));

        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        await second.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1500", Token);

        // Una lectura normal no espera: el bloqueo U es compatible con los bloqueos compartidos.
        Assert.True(await second.Technicians.AsNoTracking().AnyAsync(technician => technician.Id == technicianId, Token));

        // Otra operación que también quiere bloquear al técnico espera y, con LOCK_TIMEOUT, se rinde (error 1222).
        var timeout = await Assert.ThrowsAnyAsync<Exception>(() => new TechnicianRepository(second).FindForAssignmentAsync(technicianId, Token));
        Assert.Equal(1222, (timeout as SqlException ?? timeout.InnerException as SqlException)?.Number);

        await secondTransaction.RollbackAsync(Token);
        await firstTransaction.RollbackAsync(Token);
    }

    [Fact]
    public async Task A_deactivation_waits_for_a_concurrent_assignment_and_then_sees_its_result()
    {
        var connectionString = ConnectionString();
        await using var assignment = CreateContext(connectionString);

        // Un técnico activo con órdenes abiertas: la desactivación debe rechazarse siempre, así que la prueba no cambia datos.
        var technicianId = await assignment.ServiceOrders
            .WhereOpenForTests()
            .Where(order => order.TechnicianId != null && order.Technician!.IsActive)
            .Select(order => order.TechnicianId!.Value)
            .FirstOrDefaultAsync(Token);
        Assert.SkipWhen(technicianId == 0, "No hay técnicos activos con órdenes abiertas.");

        await using var transaction = await assignment.Database.BeginTransactionAsync(Token);
        await new TechnicianRepository(assignment).FindForAssignmentAsync(technicianId, Token);

        // La desactivación se ejecuta en otra conexión mientras la "asignación" mantiene el bloqueo durante 2 segundos.
        var clock = Stopwatch.StartNew();
        var deactivation = Task.Run(async () =>
        {
            await using var context = CreateContext(connectionString);
            return await TechnicianService(context).DeactivateAsync(technicianId, Token);
        }, Token);

        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(deactivation.IsCompleted, "La desactivación no esperó a que terminara la asignación.");
        await transaction.RollbackAsync(Token);

        var result = await deactivation;
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(2));
        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Contains("abierta", result.Message);
    }

    [Fact]
    public async Task Optimistic_concurrency_detects_a_stale_order_version()
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(Token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
        await using var first = CreateContext(connection);
        await using var second = CreateContext(connection);
        await first.Database.UseTransactionAsync(transaction, Token);
        await second.Database.UseTransactionAsync(transaction, Token);

        var orderId = await first.ServiceOrders.WhereOpenForTests().Select(order => order.Id).FirstOrDefaultAsync(Token);
        Assert.SkipWhen(orderId == 0, "No hay órdenes abiertas para probar la concurrencia.");
        var firstCopy = await first.ServiceOrders.SingleAsync(order => order.Id == orderId, Token);
        var secondCopy = await second.ServiceOrders.SingleAsync(order => order.Id == orderId, Token);
        var now = DateTime.UtcNow;

        firstCopy.UpdatePlan(ServiceOrderPriority.Urgent, firstCopy.PromisedAtUtc, firstCopy.EstimatedCost, now);
        await first.SaveChangesAsync(Token);
        secondCopy.UpdatePlan(ServiceOrderPriority.Low, secondCopy.PromisedAtUtc, secondCopy.EstimatedCost, now);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(second).SaveChangesAsync(Token));
        await transaction.RollbackAsync(Token);
    }

    [Fact]
    public async Task Order_search_ignores_accents_and_case_with_the_spanish_collation()
    {
        await using var context = CreateContext(ConnectionString());
        var name = await context.ServiceOrders.Select(order => order.Device!.Customer!.FullName).FirstOrDefaultAsync(Token);
        Assert.SkipWhen(name is null || !name.Any(c => "áéíóúÁÉÍÓÚ".Contains(c)), "No hay clientes con tildes y órdenes para probar la búsqueda.");

        var withoutAccents = new string(name!.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();

        var result = await new ServiceOrderRepository(context).SearchAsync(
            new ServiceOrderSearch(withoutAccents, ServiceOrderView.All), DateTime.UtcNow, Token);

        Assert.Contains(result.Page.Items, order => order.CustomerName == name);
    }

    /// <summary>Servicio de técnicos real sobre un contexto propio, como en una solicitud web.</summary>
    private static ITechnicianService TechnicianService(FixTrackDbContext context) =>
        new ServiceCollection()
            .AddApplication()
            .AddSingleton<ITechnicianRepository>(new TechnicianRepository(context))
            .AddSingleton<IUnitOfWork>(new EfUnitOfWork(context))
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider()
            .GetRequiredService<ITechnicianService>();
}

internal static class SqlServerTestExtensions
{
    public static IQueryable<ServiceOrder> WhereOpenForTests(this IQueryable<ServiceOrder> orders) =>
        orders.Where(order => order.Status != ServiceOrderStatus.Delivered && order.Status != ServiceOrderStatus.Cancelled);
}
