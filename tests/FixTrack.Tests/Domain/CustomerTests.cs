using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;

namespace FixTrack.Tests.Domain;

public sealed class CustomerTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_customer_is_active_and_its_data_is_normalized()
    {
        var customer = new Customer("  Ana   López ", " 5555-1234 ", " Ana@Example.com ", "  ", Now);

        Assert.Equal("Ana López", customer.FullName);
        Assert.Equal("5555-1234", customer.Phone);
        Assert.Equal("ana@example.com", customer.Email);
        Assert.Null(customer.Notes);
        Assert.True(customer.IsActive);
        Assert.Equal(Now, customer.CreatedAtUtc);
        Assert.Null(customer.UpdatedAtUtc);
    }

    [Fact]
    public void Invalid_data_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => new Customer("Ana López", "12", null, null, Now));
        Assert.Contains("teléfono", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dates_must_be_utc() =>
        Assert.Throws<ArgumentException>(() => new Customer("Ana López", "5555-1234", null, null, DateTime.Now));

    [Fact]
    public void Deactivating_keeps_the_record_and_stores_when_it_happened()
    {
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        var later = Now.AddDays(3);

        customer.Deactivate(later);

        Assert.False(customer.IsActive);
        Assert.Equal(later, customer.DeactivatedAtUtc);
        Assert.Equal(later, customer.UpdatedAtUtc);
        Assert.Equal("Ana López", customer.FullName);
    }

    [Fact]
    public void Reactivating_clears_the_deactivation_date()
    {
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);
        customer.Deactivate(Now.AddDays(1));

        customer.Reactivate(Now.AddDays(2));

        Assert.True(customer.IsActive);
        Assert.Null(customer.DeactivatedAtUtc);
        Assert.Equal(Now.AddDays(2), customer.UpdatedAtUtc);
    }

    [Fact]
    public void Status_changes_must_be_meaningful()
    {
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);

        Assert.Throws<DomainException>(() => customer.Reactivate(Now));
        customer.Deactivate(Now);
        Assert.Throws<DomainException>(() => customer.Deactivate(Now));
    }

    [Fact]
    public void Updating_details_records_the_update_time()
    {
        var customer = new Customer("Ana López", "5555-1234", null, null, Now);

        customer.UpdateDetails("Ana María López", "+502 5555 1234", "ana@example.com", "Prefiere WhatsApp", Now.AddHours(1));

        Assert.Equal("Ana María López", customer.FullName);
        Assert.Equal("+502 5555 1234", customer.Phone);
        Assert.Equal("Prefiere WhatsApp", customer.Notes);
        Assert.Equal(Now.AddHours(1), customer.UpdatedAtUtc);
    }
}
