using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Domain.Customers;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;

namespace FixTrack.Tests.Infrastructure;

public sealed class CustomerRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync(params (string Name, string Phone, string? Email, bool Active)[] customers)
    {
        await using var context = _database.CreateContext();
        foreach (var (name, phone, email, active) in customers)
        {
            var customer = new Customer(name, phone, email, null, Now);
            if (!active)
            {
                customer.Deactivate(Now);
            }

            context.Customers.Add(customer);
        }

        await context.SaveChangesAsync(Token);
    }

    private async Task<CustomerSearchResult> SearchAsync(CustomerSearch search)
    {
        await using var context = _database.CreateContext();
        return await new CustomerRepository(context).SearchAsync(search, Token);
    }

    [Fact]
    public async Task Lists_active_customers_sorted_by_name_with_counts_per_status()
    {
        await SeedAsync(
            ("Carlos Méndez", "5555-0003", null, true),
            ("Ana López", "5555-0001", null, true),
            ("Beatriz Soto", "5555-0002", null, false));

        var result = await SearchAsync(new CustomerSearch(null));

        Assert.Equal(["Ana López", "Carlos Méndez"], result.Page.Items.Select(c => c.FullName));
        Assert.Equal(2, result.ActiveCount);
        Assert.Equal(1, result.InactiveCount);
        Assert.Equal(2, result.Page.TotalCount);
    }

    [Theory]
    [InlineData("lopez", "Ana Lopez")]
    [InlineData("0003", "Carlos Mendez")]
    [InlineData("example.com", "Ana Lopez")]
    public async Task Searches_by_name_phone_or_email(string text, string expected)
    {
        await SeedAsync(
            ("Ana Lopez", "5555-0001", "ana@example.com", true),
            ("Carlos Mendez", "5555-0003", "carlos@correo.test", true));

        var result = await SearchAsync(new CustomerSearch(text));

        Assert.Equal(expected, Assert.Single(result.Page.Items).FullName);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a-z]")]
    public async Task Wildcards_typed_by_the_user_are_searched_literally(string text)
    {
        await SeedAsync(("Ana Lopez", "5555-0001", "ana@example.com", true));

        var result = await SearchAsync(new CustomerSearch(text));

        Assert.Empty(result.Page.Items);
    }

    [Fact]
    public async Task Filters_inactive_customers_and_paginates()
    {
        await SeedAsync([.. Enumerable.Range(1, 23).Select(i => ($"Cliente Inactivo {i:D2}", $"5555-{i:D4}", (string?)null, false))]);

        var firstPage = await SearchAsync(new CustomerSearch(null, StatusFilter.Inactive, Page: 1));
        var lastPage = await SearchAsync(new CustomerSearch(null, StatusFilter.Inactive, Page: 99));

        Assert.Equal(CustomerSearch.PageSize, firstPage.Page.Items.Count);
        Assert.Equal(2, firstPage.Page.TotalPages);
        Assert.Equal(2, lastPage.Page.Page);
        Assert.Equal(3, lastPage.Page.Items.Count);
        Assert.Equal(21, lastPage.Page.FirstItemNumber);
    }

    [Fact]
    public async Task Email_lookups_can_exclude_the_customer_being_edited()
    {
        await SeedAsync(("Ana Lopez", "5555-0001", "ana@example.com", true));
        await using var context = _database.CreateContext();
        var repository = new CustomerRepository(context);

        Assert.True(await repository.EmailExistsAsync("ana@example.com", null, Token));
        Assert.False(await repository.EmailExistsAsync("ana@example.com", excludingCustomerId: 1, Token));
        Assert.False(await repository.EmailExistsAsync("otra@example.com", null, Token));
    }
}
