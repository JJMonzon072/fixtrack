using FixTrack.Domain.Customers;

namespace FixTrack.Application.Customers;

/// <summary>Acceso a los clientes. Lo implementa la capa de infraestructura.</summary>
public interface ICustomerRepository
{
    Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken);

    Task<CustomerDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Obtiene el cliente para modificarlo.</summary>
    Task<Customer?> FindAsync(int id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, int? excludingCustomerId, CancellationToken cancellationToken);

    void Add(Customer customer);
}
