using FixTrack.Application.Devices;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

public sealed class DeviceRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    /// <summary>Ana (activa) con una laptop Dell y una impresora HP; Mario (inactivo) con una tableta Samsung.</summary>
    private async Task<(int AnaId, int MarioId)> SeedAsync()
    {
        await using var context = _database.CreateContext();
        var ana = new Customer("Ana Lopez", "5555-0001", null, null, Now);
        var mario = new Customer("Mario Castillo", "5555-0002", null, null, Now);
        context.Customers.AddRange(ana, mario);
        await context.SaveChangesAsync(Token);

        ana.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude 5440", "DL-5440", null, Now);
        ana.RegisterDevice(DeviceType.Printer, "HP", "LaserJet Pro M404", null, null, Now.AddMinutes(1));
        mario.RegisterDevice(DeviceType.Tablet, "Samsung", "Galaxy Tab S9", "SM-X710", null, Now.AddMinutes(2));
        mario.Deactivate(Now.AddDays(1));
        await context.SaveChangesAsync(Token);
        return (ana.Id, mario.Id);
    }

    private async Task<IReadOnlyList<string>> SearchBrandsAsync(DeviceSearch search)
    {
        await using var context = _database.CreateContext();
        var result = await new DeviceRepository(context).SearchAsync(search, Token);
        return [.. result.Items.Select(item => item.Brand)];
    }

    [Theory]
    [InlineData("dell", "Dell")]
    [InlineData("laserjet", "HP")]
    [InlineData("dell latitude", "Dell")]
    [InlineData("sm-x710", "Samsung")]
    [InlineData("castillo", "Samsung")]
    public async Task Searches_by_brand_model_serial_number_or_customer(string text, string expectedBrand)
    {
        await SeedAsync();

        Assert.Equal([expectedBrand], await SearchBrandsAsync(new DeviceSearch(text)));
    }

    [Fact]
    public async Task Lists_newest_first_including_devices_of_inactive_customers()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var result = await new DeviceRepository(context).SearchAsync(new DeviceSearch(null), Token);

        Assert.Equal(["Samsung", "HP", "Dell"], result.Items.Select(item => item.Brand));
        var tablet = result.Items[0];
        Assert.Equal("Mario Castillo", tablet.CustomerName);
        Assert.False(tablet.CustomerIsActive);
        Assert.Equal(0, tablet.ServiceOrderCount);
    }

    [Fact]
    public async Task Filters_by_type()
    {
        await SeedAsync();

        Assert.Equal(["HP"], await SearchBrandsAsync(new DeviceSearch(null, DeviceType.Printer)));
        Assert.Empty(await SearchBrandsAsync(new DeviceSearch("dell", DeviceType.Printer)));
    }

    [Fact]
    public async Task Paginates_in_the_database()
    {
        await using (var context = _database.CreateContext())
        {
            var customer = new Customer("Taller Prueba", "5555-0003", null, null, Now);
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Token);
            for (var i = 1; i <= 23; i++)
            {
                customer.RegisterDevice(DeviceType.Monitor, $"Marca {i:D2}", null, null, null, Now.AddMinutes(i));
            }

            await context.SaveChangesAsync(Token);
        }

        await using var reader = _database.CreateContext();
        var repository = new DeviceRepository(reader);
        var first = await repository.SearchAsync(new DeviceSearch(null, Page: 1), Token);
        var last = await repository.SearchAsync(new DeviceSearch(null, Page: 50), Token);

        Assert.Equal(23, first.TotalCount);
        Assert.Equal(DeviceSearch.PageSize, first.Items.Count);
        Assert.Equal("Marca 23", first.Items[0].Brand);
        Assert.Equal(2, last.Page);
        Assert.Equal(["Marca 03", "Marca 02", "Marca 01"], last.Items.Select(item => item.Brand));
    }

    [Fact]
    public async Task Lists_the_devices_of_a_customer_and_their_details()
    {
        var (anaId, marioId) = await SeedAsync();
        await using var context = _database.CreateContext();
        var repository = new DeviceRepository(context);

        var anaDevices = await repository.ListByCustomerAsync(anaId, Token);
        var marioDevices = await repository.ListByCustomerAsync(marioId, Token);
        var details = await repository.GetDetailsAsync(marioDevices[0].Id, Token);

        Assert.Equal(["HP", "Dell"], anaDevices.Select(device => device.Brand));
        Assert.Equal("Samsung", Assert.Single(marioDevices).Brand);
        Assert.NotNull(details);
        Assert.Equal("Mario Castillo", details.Owner.FullName);
        Assert.False(details.Owner.IsActive);
        Assert.Empty(details.ServiceOrders);
        Assert.Null(await repository.GetDetailsAsync(9999, Token));
    }

    [Fact]
    public async Task Device_details_include_their_service_orders()
    {
        var (anaId, _) = await SeedAsync();
        await using (var context = _database.CreateContext())
        {
            var deviceId = (await new DeviceRepository(context).ListByCustomerAsync(anaId, Token))[0].Id;
            context.ServiceOrders.Add(new ServiceOrder(deviceId, "No imprime", ServiceOrderPriority.High, Now, Now.AddDays(2), 150m));
            await context.SaveChangesAsync(Token);
        }

        await using var reader = _database.CreateContext();
        var repository = new DeviceRepository(reader);
        var devices = await repository.ListByCustomerAsync(anaId, Token);
        var details = await repository.GetDetailsAsync(devices[0].Id, Token);

        Assert.Equal(1, devices[0].ServiceOrderCount);
        var order = Assert.Single(details!.ServiceOrders);
        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Equal(ServiceOrderPriority.High, order.Priority);
    }

    [Fact]
    public async Task Detects_serial_numbers_already_used_by_the_same_brand()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();
        var repository = new DeviceRepository(context);

        Assert.True(await repository.SerialNumberExistsAsync("Dell", "DL-5440", null, Token));
        Assert.False(await repository.SerialNumberExistsAsync("HP", "DL-5440", null, Token));
        Assert.False(await repository.SerialNumberExistsAsync("Dell", "DL-5440", excludingDeviceId: 1, Token));
    }
}
