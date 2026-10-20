using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>Consultas de clientes con EF Core. Todas las búsquedas se envían parametrizadas.</summary>
internal sealed class CustomerRepository(FixTrackDbContext db) : ICustomerRepository
{
    public async Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        var matching = db.Customers.AsNoTracking();
        if (search.Text is { } text)
        {
            matching = WhereMatches(matching, text);
        }

        var counts = await matching
            .GroupBy(customer => customer.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var activeCount = counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0;
        var inactiveCount = counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0;

        var (filtered, total) = search.Status switch
        {
            StatusFilter.Inactive => (matching.Where(customer => !customer.IsActive), inactiveCount),
            StatusFilter.All => (matching, activeCount + inactiveCount),
            _ => (matching.Where(customer => customer.IsActive), activeCount),
        };

        // Si la página pedida ya no existe (por ejemplo, tras desactivar clientes), se muestra la última.
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)CustomerSearch.PageSize));
        var page = Math.Min(search.Page, lastPage);

        var items = await filtered
            .OrderBy(customer => customer.FullName)
            .ThenBy(customer => customer.Id)
            .Skip((page - 1) * CustomerSearch.PageSize)
            .Take(CustomerSearch.PageSize)
            .Select(customer => new CustomerListItem(
                customer.Id,
                customer.FullName,
                customer.Phone,
                customer.Email,
                customer.IsActive,
                customer.Devices.Count,
                customer.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new CustomerSearchResult(
            new PagedResult<CustomerListItem>(items, page, CustomerSearch.PageSize, total),
            activeCount,
            inactiveCount);
    }

    public Task<CustomerDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
        db.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == id)
            .Select(customer => new CustomerDetails(
                customer.Id,
                customer.FullName,
                customer.Phone,
                customer.Email,
                customer.Notes,
                customer.IsActive,
                customer.Devices.Count,
                customer.CreatedAtUtc,
                customer.UpdatedAtUtc,
                customer.DeactivatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Customer?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.Customers.SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, int? excludingCustomerId, CancellationToken cancellationToken) =>
        db.Customers.AnyAsync(
            customer => customer.Email == email && (excludingCustomerId == null || customer.Id != excludingCustomerId),
            cancellationToken);

    public async Task<IReadOnlyList<CustomerOption>> SuggestActiveAsync(string? text, int limit, CancellationToken cancellationToken)
    {
        var active = db.Customers.AsNoTracking().Where(customer => customer.IsActive);
        if (text is not null)
        {
            active = WhereMatches(active, text);
        }

        return await active
            .OrderBy(customer => customer.FullName)
            .ThenBy(customer => customer.Id)
            .Take(limit)
            .Select(customer => new CustomerOption(customer.Id, customer.FullName, customer.Phone, customer.Email, customer.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerOption?> GetOptionAsync(int id, CancellationToken cancellationToken) =>
        db.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == id)
            .Select(customer => new CustomerOption(customer.Id, customer.FullName, customer.Phone, customer.Email, customer.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(Customer customer) => db.Customers.Add(customer);

    // En SQL Server el nombre respeta su intercalación: sin distinguir mayúsculas ni tildes.
    private static IQueryable<Customer> WhereMatches(IQueryable<Customer> customers, string text)
    {
        var pattern = LikePatterns.Contains(text);
        return customers.Where(customer =>
            EF.Functions.Like(customer.FullName, pattern, LikePatterns.Escape)
            || EF.Functions.Like(customer.Phone, pattern, LikePatterns.Escape)
            || (customer.Email != null && EF.Functions.Like(customer.Email, pattern, LikePatterns.Escape)));
    }
}
