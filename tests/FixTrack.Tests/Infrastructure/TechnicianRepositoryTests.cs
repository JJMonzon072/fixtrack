using FixTrack.Application.Common;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

public sealed class TechnicianRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync()
    {
        await using var context = _database.CreateContext();
        var carla = new Technician("Carla Ruiz", "Impresoras y periféricos", "5555-0301", "carla@example.com", Now);
        var bruno = new Technician("Bruno Soto", "Redes", null, null, Now);
        var andres = new Technician("Andres Mejia", "Laptops", "5555-0302", null, Now);
        andres.Deactivate(Now.AddDays(1));
        context.Technicians.AddRange(carla, bruno, andres);
        await context.SaveChangesAsync(Token);
    }

    private async Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search)
    {
        await using var context = _database.CreateContext();
        return await new TechnicianRepository(context).SearchAsync(search, Token);
    }

    [Fact]
    public async Task Lists_active_technicians_by_name_with_counts_per_status()
    {
        await SeedAsync();

        var result = await SearchAsync(new TechnicianSearch(null));

        Assert.Equal(["Bruno Soto", "Carla Ruiz"], result.Page.Items.Select(t => t.FullName));
        Assert.Equal(2, result.ActiveCount);
        Assert.Equal(1, result.InactiveCount);
    }

    [Theory]
    [InlineData("impresoras", "Carla Ruiz")]
    [InlineData("0302", "Andres Mejia")]
    [InlineData("carla@", "Carla Ruiz")]
    [InlineData("bruno", "Bruno Soto")]
    public async Task Searches_by_name_specialty_phone_or_email(string text, string expected)
    {
        await SeedAsync();

        var result = await SearchAsync(new TechnicianSearch(text, StatusFilter.All));

        Assert.Equal(expected, Assert.Single(result.Page.Items).FullName);
    }

    [Fact]
    public async Task Filters_inactive_technicians()
    {
        await SeedAsync();

        var result = await SearchAsync(new TechnicianSearch(null, StatusFilter.Inactive));

        Assert.Equal("Andres Mejia", Assert.Single(result.Page.Items).FullName);
    }

    [Fact]
    public async Task Counts_only_open_orders_assigned_to_the_technician()
    {
        await SeedAsync();
        int technicianId;
        await using (var context = _database.CreateContext())
        {
            var technician = context.Technicians.Single(t => t.FullName == "Carla Ruiz");
            technicianId = technician.Id;
            var customer = new Customer("Ana Lopez", "5555-0001", null, null, Now);
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Token);
            var device = customer.RegisterDevice(DeviceType.Printer, "HP", null, null, null, Now);
            await context.SaveChangesAsync(Token);

            var open = new ServiceOrder(device, "No imprime", ServiceOrderPriority.Normal, Now, null, null, null, technician);
            var cancelled = new ServiceOrder(device, "Atasco de papel", ServiceOrderPriority.Low, Now, null, null, null, technician);
            cancelled.ChangeStatus(ServiceOrderStatus.Cancelled, Now.AddHours(1), "Orden duplicada");
            context.ServiceOrders.AddRange(open, cancelled);
            await context.SaveChangesAsync(Token);
        }

        await using var reader = _database.CreateContext();
        var repository = new TechnicianRepository(reader);
        var details = await repository.GetDetailsAsync(technicianId, Token);

        Assert.Equal(1, await repository.CountOpenOrdersAsync(technicianId, Token));
        Assert.Equal(1, details!.OpenOrderCount);
        Assert.Equal(2, details.TotalOrderCount);
        Assert.Equal(ServiceOrderStatus.Received, Assert.Single(details.OpenOrders).Status);
    }

    [Fact]
    public async Task Email_lookups_can_exclude_the_technician_being_edited()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();
        var repository = new TechnicianRepository(context);
        var carlaId = context.Technicians.Single(t => t.Email == "carla@example.com").Id;

        Assert.True(await repository.EmailExistsAsync("carla@example.com", null, Token));
        Assert.False(await repository.EmailExistsAsync("carla@example.com", carlaId, Token));
    }
}
