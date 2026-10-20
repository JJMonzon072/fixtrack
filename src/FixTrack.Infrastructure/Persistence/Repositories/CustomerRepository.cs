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
            // LIKE parametrizado con los comodines escapados: el texto del usuario se busca literalmente.
            // En SQL Server respeta la intercalación de la columna (sin distinguir mayúsculas ni tildes en el nombre).
            var pattern = ContainsPattern(text);
            matching = matching.Where(customer =>
                EF.Functions.Like(customer.FullName, pattern, LikeEscape)
                || EF.Functions.Like(customer.Phone, pattern, LikeEscape)
                || (customer.Email != null && EF.Functions.Like(customer.Email, pattern, LikeEscape)));
        }

        var counts = await matching
            .GroupBy(customer => customer.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var activeCount = counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0;
        var inactiveCount = counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0;

        var (filtered, total) = search.Status switch
        {
            CustomerStatusFilter.Inactive => (matching.Where(customer => !customer.IsActive), inactiveCount),
            CustomerStatusFilter.All => (matching, activeCount + inactiveCount),
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

    public void Add(Customer customer) => db.Customers.Add(customer);

    private const string LikeEscape = "\\";

    private static string ContainsPattern(string text) =>
        "%" + text
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal) + "%";
}
