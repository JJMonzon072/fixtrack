using FixTrack.Application.Common;

namespace FixTrack.Application.Customers;

/// <summary>Casos de uso del módulo Clientes.</summary>
public interface ICustomerService
{
    Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken);

    Task<CustomerDetails?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Registra un cliente. Si tiene éxito, devuelve su Id.</summary>
    Task<CommandResult<int>> CreateAsync(CustomerInput input, CancellationToken cancellationToken);

    Task<CommandResult> UpdateAsync(int id, CustomerInput input, CancellationToken cancellationToken);

    Task<CommandResult> DeactivateAsync(int id, CancellationToken cancellationToken);

    Task<CommandResult> ReactivateAsync(int id, CancellationToken cancellationToken);
}
