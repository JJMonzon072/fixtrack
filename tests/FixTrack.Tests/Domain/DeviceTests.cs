using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;

namespace FixTrack.Tests.Domain;

public sealed class DeviceTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private static Customer ActiveCustomer() => new("Ana López", "5555-1234", null, null, Now);

    [Fact]
    public void An_active_customer_registers_a_device_with_normalized_data()
    {
        var customer = ActiveCustomer();

        var device = customer.RegisterDevice(DeviceType.Laptop, "  Dell ", " Latitude   5440 ", " sn-ab 12/3 ", "  Con cargador  ", Now);

        Assert.Same(customer, device.Customer);
        Assert.Contains(device, customer.Devices);
        Assert.Equal(DeviceType.Laptop, device.Type);
        Assert.Equal("Dell", device.Brand);
        Assert.Equal("Latitude 5440", device.Model);
        Assert.Equal("SN-AB 12/3", device.SerialNumber);
        Assert.Equal("Con cargador", device.Notes);
        Assert.Equal(Now, device.CreatedAtUtc);
        Assert.Null(device.UpdatedAtUtc);
    }

    [Fact]
    public void An_inactive_customer_cannot_receive_new_devices_but_keeps_the_existing_ones()
    {
        var customer = ActiveCustomer();
        var existing = customer.RegisterDevice(DeviceType.Printer, "HP", null, null, null, Now);
        customer.Deactivate(Now.AddDays(1));

        var error = Assert.Throws<DomainException>(() =>
            customer.RegisterDevice(DeviceType.Laptop, "Lenovo", null, null, null, Now.AddDays(2)));

        Assert.Contains("inactivo", error.Message);
        Assert.Equal([existing], customer.Devices);
    }

    [Theory]
    [InlineData((DeviceType)0, "Dell", null, "tipo")]
    [InlineData((DeviceType)99, "Dell", null, "tipo")]
    [InlineData(DeviceType.Laptop, "   ", null, "marca es obligatoria")]
    [InlineData(DeviceType.Laptop, "---", null, "letras o números")]
    [InlineData(DeviceType.Laptop, "Dell", "SÉRIE-Ñ", "número de serie solo admite")]
    [InlineData(DeviceType.Laptop, "Dell", "-ABC", "número de serie solo admite")]
    public void Invalid_device_data_is_rejected(DeviceType type, string brand, string? serial, string expected)
    {
        var error = Assert.Throws<DomainException>(() =>
            ActiveCustomer().RegisterDevice(type, brand, null, serial, null, Now));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Length_limits_are_enforced()
    {
        var errors = DeviceRules.Validate(
            DeviceType.Laptop,
            new string('a', DeviceRules.BrandMaxLength + 1),
            new string('a', DeviceRules.ModelMaxLength + 1),
            new string('A', DeviceRules.SerialNumberMaxLength + 1),
            new string('a', DeviceRules.NotesMaxLength + 1));

        Assert.Equal(
            [nameof(Device.Brand), nameof(Device.Model), nameof(Device.Notes), nameof(Device.SerialNumber)],
            errors.Keys.Order());
    }

    [Fact]
    public void Updating_a_device_changes_its_technical_data_but_never_its_owner()
    {
        var customer = ActiveCustomer();
        var device = customer.RegisterDevice(DeviceType.Laptop, "Dell", "Latitude", null, null, Now);

        device.UpdateDetails(DeviceType.AllInOne, "Dell", "OptiPlex 7410", "ab123", "Pantalla rayada", Now.AddHours(2));

        Assert.Equal(DeviceType.AllInOne, device.Type);
        Assert.Equal("OptiPlex 7410", device.Model);
        Assert.Equal("AB123", device.SerialNumber);
        Assert.Equal("Pantalla rayada", device.Notes);
        Assert.Equal(Now.AddHours(2), device.UpdatedAtUtc);
        Assert.Same(customer, device.Customer);
    }

    [Fact]
    public void An_empty_serial_number_is_stored_as_missing() =>
        Assert.Null(ActiveCustomer().RegisterDevice(DeviceType.Monitor, "LG", null, "   ", null, Now).SerialNumber);

    [Fact]
    public void Dates_must_be_utc() =>
        Assert.Throws<ArgumentException>(() =>
            ActiveCustomer().RegisterDevice(DeviceType.Monitor, "LG", null, null, null, DateTime.Now));
}
