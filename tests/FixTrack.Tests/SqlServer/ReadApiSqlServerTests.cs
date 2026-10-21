using FixTrack.Application.ReadApi;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;
using FixTrack.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Tests.SqlServer;

/// <summary>Listados de la API contra SQL Server real: filtros, orden (con fechas nulas al final) y página en SQL.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReadApiSqlServerTests(DisposableSqlServerDatabase database)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FixTrackDbContext Context(string connectionString, CommandCounter? counter = null)
    {
        var options = new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlServer(connectionString);
        if (counter is not null)
        {
            options.AddInterceptors(counter);
        }

        return new FixTrackDbContext(options.Options);
    }

    [Fact]
    public async Task Orders_and_devices_are_filtered_sorted_and_paged_in_sql()
    {
        Assert.SkipUnless(DisposableSqlServerDatabase.IsConfigured, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");
        var connectionString = await database.CreateEmptyDatabaseAsync();
        await using (var context = Context(connectionString))
        {
            await context.Database.MigrateAsync(Token);
        }

        await ReportSeed.SeedAsync(() => Context(connectionString), Token);
        var counter = new CommandCounter();
        await using var db = Context(connectionString, counter);
        var queries = new ReadApiQueries(db);

        var byPromise = await queries.ListOrdersAsync(new OrderListQuery(PageSize: 100, Sort: OrderSortField.Promised, Descending: false), Token);
        var byPriority = await queries.ListOrdersAsync(new OrderListQuery(PageSize: 100, Sort: OrderSortField.Priority), Token);
        var secondPage = await queries.ListOrdersAsync(new OrderListQuery(Page: 2, PageSize: 4, Sort: OrderSortField.Number, Descending: false), Token);
        var filtered = await queries.ListOrdersAsync(
            new OrderListQuery(Status: ServiceOrderStatus.Delivered, CustomerId: 1, ReceivedFromUtc: new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc), Sort: OrderSortField.Number, Descending: false),
            Token);
        counter.Reset();
        var devices = await queries.ListDevicesAsync(new DeviceListQuery(Type: DeviceType.Laptop, Sort: DeviceSortField.Brand, Descending: false), Token);

        Assert.Equal([1, 5, 11, 2, 3, 4, 6, 7, 8, 9, 10], byPromise.Items.Select(order => order.Id));
        Assert.Equal([9, 3, 8, 1, 11, 10, 7, 5, 2, 6, 4], byPriority.Items.Select(order => order.Id));
        Assert.Equal([5, 6, 7, 8], secondPage.Items.Select(order => order.Id));
        Assert.Equal((2, 4, 11), (secondPage.Page, secondPage.PageSize, secondPage.TotalCount));
        Assert.Equal([1, 5, 11], filtered.Items.Select(order => order.Id));
        var laptop = Assert.Single(devices.Items);
        Assert.Equal(("Dell", 5, "Ana Morales"), (laptop.Brand, laptop.ServiceOrderCount, laptop.CustomerName));
        Assert.Equal(2, counter.Count); // el conteo y la página, con la cantidad de órdenes como subconsulta: sin N+1.
    }
}
