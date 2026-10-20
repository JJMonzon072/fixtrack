using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

/// <summary>Casos de uso de equipos con repositorios en memoria: sin base de datos.</summary>
public sealed class DeviceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FakeDeviceRepository _devices;
    private readonly IDeviceService _service;

    public DeviceServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_store);
        _devices = new FakeDeviceRepository(_store);
        _service = new ServiceCollection()
            .AddApplication()
            .AddSingleton<ICustomerRepository>(new FakeCustomerRepository(_store))
            .AddSingleton<IDeviceRepository>(_devices)
            .AddSingleton<IUnitOfWork>(_unitOfWork)
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .BuildServiceProvider()
            .GetRequiredService<IDeviceService>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Customer AddCustomer(string name, bool active = true)
    {
        var customer = new Customer(name, "5555-1234", null, null, Now.UtcDateTime);
        if (!active)
        {
            customer.Deactivate(Now.UtcDateTime);
        }

        InMemoryStore.AssignId(customer, _store.Customers.Count + 1);
        _store.Customers.Add(customer);
        return customer;
    }

    private static DeviceInput Laptop(string? serial = "SN-001") => new(DeviceType.Laptop, "Dell", "Latitude 5440", serial, null);

    [Fact]
    public async Task Registers_a_device_for_an_active_customer()
    {
        var customer = AddCustomer("Ana López");

        var result = await _service.RegisterAsync(new RegisterDeviceInput(customer.Id, Laptop("sn-001")), Token);

        Assert.True(result.Succeeded);
        var device = Assert.Single(_store.Devices);
        Assert.Equal(result.Value, device.Id);
        Assert.Same(customer, device.Customer);
        Assert.Equal("SN-001", device.SerialNumber);
        Assert.Equal(Now.UtcDateTime, device.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task A_device_needs_an_existing_owner(int? customerId)
    {
        var result = await _service.RegisterAsync(new RegisterDeviceInput(customerId, Laptop()), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains("cliente propietario", result.Errors[DeviceService.CustomerField]);
        Assert.Empty(_store.Devices);
    }

    [Fact]
    public async Task An_inactive_customer_cannot_receive_new_devices()
    {
        var customer = AddCustomer("María Ruiz", active: false);

        var result = await _service.RegisterAsync(new RegisterDeviceInput(customer.Id, Laptop()), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains("inactivo", result.Errors[DeviceService.CustomerField]);
        Assert.Empty(_store.Devices);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Returns_every_validation_error_at_once()
    {
        var result = await _service.RegisterAsync(
            new RegisterDeviceInput(null, new DeviceInput(null, "", null, "ÑÑ", new string('x', 501))),
            Token);

        Assert.Equal(
            [nameof(Device.Brand), DeviceService.CustomerField, nameof(Device.Notes), nameof(Device.SerialNumber), nameof(Device.Type)],
            result.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Serial_numbers_are_unique_per_brand_ignoring_case()
    {
        var customer = AddCustomer("Ana López");
        await _service.RegisterAsync(new RegisterDeviceInput(customer.Id, Laptop("SN-001")), Token);

        var sameBrand = await _service.RegisterAsync(
            new RegisterDeviceInput(customer.Id, new DeviceInput(DeviceType.Laptop, "DELL", null, "sn-001", null)), Token);
        var otherBrand = await _service.RegisterAsync(
            new RegisterDeviceInput(customer.Id, new DeviceInput(DeviceType.Printer, "HP", null, "SN-001", null)), Token);

        Assert.Equal(DeviceService.DuplicateSerialMessage, sameBrand.Errors[nameof(Device.SerialNumber)]);
        Assert.True(otherBrand.Succeeded);
        Assert.Equal(2, _store.Devices.Count);
    }

    [Fact]
    public async Task A_duplicate_detected_by_the_database_becomes_a_field_error()
    {
        var customer = AddCustomer("Ana López");
        _unitOfWork.FailNextSaveWithDuplicate = true;

        var result = await _service.RegisterAsync(new RegisterDeviceInput(customer.Id, Laptop()), Token);

        Assert.Equal(DeviceService.DuplicateSerialMessage, result.Errors[nameof(Device.SerialNumber)]);
    }

    [Fact]
    public async Task Editing_keeps_the_owner_and_allows_keeping_the_same_serial_number()
    {
        var owner = AddCustomer("Ana López");
        var created = await _service.RegisterAsync(new RegisterDeviceInput(owner.Id, Laptop("SN-001")), Token);

        var result = await _service.UpdateAsync(
            created.Value,
            new DeviceInput(DeviceType.Laptop, "Dell", "Latitude 7450", "SN-001", "Cambio de teclado"),
            version: 0,
            Token);

        Assert.True(result.Succeeded);
        var device = Assert.Single(_store.Devices);
        Assert.Equal("Latitude 7450", device.Model);
        Assert.Equal("Cambio de teclado", device.Notes);
        Assert.Equal(Now.UtcDateTime, device.UpdatedAtUtc);
        Assert.Same(owner, device.Customer);
    }

    [Fact]
    public async Task Devices_of_an_inactive_customer_can_still_be_edited()
    {
        var owner = AddCustomer("Ana López");
        var created = await _service.RegisterAsync(new RegisterDeviceInput(owner.Id, Laptop()), Token);
        owner.Deactivate(Now.UtcDateTime);

        var result = await _service.UpdateAsync(created.Value, Laptop() with { Notes = "Equipo entregado" }, version: 0, Token);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Unknown_devices_are_reported_as_not_found()
    {
        Assert.Equal(CommandStatus.NotFound, (await _service.UpdateAsync(99, Laptop(), version: 0, Token)).Status);
        Assert.Null(await _service.GetAsync(0, Token));
    }

    [Fact]
    public async Task A_stale_edit_is_rejected_without_changes()
    {
        var owner = AddCustomer("Ana López");
        var created = await _service.RegisterAsync(new RegisterDeviceInput(owner.Id, Laptop()), Token);
        Assert.True((await _service.UpdateAsync(created.Value, Laptop() with { Notes = "Primera edición" }, version: 0, Token)).Succeeded);

        var stale = await _service.UpdateAsync(created.Value, Laptop() with { Notes = "Edición desactualizada" }, version: 0, Token);

        Assert.Equal(CommandStatus.Conflict, stale.Status);
        Assert.Equal(DeviceService.StaleMessage, stale.Message);
        Assert.Equal("Primera edición", _store.Devices.Single().Notes);
    }

    [Fact]
    public async Task Search_criteria_are_normalized_before_querying()
    {
        await _service.SearchAsync(new DeviceSearch("  " + new string('a', 150), (DeviceType)42, Page: -1), Token);

        var search = _devices.LastSearch!;
        Assert.Equal(DeviceSearch.TextMaxLength, search.Text!.Length);
        Assert.Null(search.Type);
        Assert.Equal(1, search.Page);
    }
}
