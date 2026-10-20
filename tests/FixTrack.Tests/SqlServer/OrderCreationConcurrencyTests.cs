using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.SqlServer;

/// <summary>Dos envíos simultáneos del mismo formulario de nueva orden registran una sola orden (SQL Server real).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class OrderCreationConcurrencyTests(DisposableSqlServerDatabase database) : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _app;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (database.ConnectionString is { } connectionString)
        {
            _app = await DisposableApp.CreateAsync(connectionString);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Simultaneous_submissions_of_the_same_form_register_one_order()
    {
        Assert.SkipWhen(_app is null, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar esta prueba.");
        int customerId, deviceId;
        await using (var context = database.CreateContext())
        {
            var device = await context.Devices.Where(d => d.Customer!.IsActive).Select(d => new { d.Id, d.CustomerId }).FirstAsync(Token);
            (customerId, deviceId) = (device.CustomerId, device.Id);
        }

        var requestId = Guid.NewGuid();
        var input = new ServiceOrderInput(customerId, deviceId, "Envío doble", ServiceOrderPriority.Normal, null, null, null, null, requestId);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissions = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await using var scope = _app!.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IServiceOrderService>().CreateAsync(input, Token);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(submissions);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Single(results.Select(result => result.Value).Distinct());
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.ServiceOrders.CountAsync(order => order.CreationRequestId == requestId, Token));
        Assert.Equal(1, await check.AuditEvents.CountAsync(audit => audit.EntityId == results[0].Value.ToString() && audit.Action == "orden.registrada", Token));
    }
}
