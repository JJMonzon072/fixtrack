using FixTrack.Application.Common;

namespace FixTrack.Application.Customers;

/// <summary>Casos de uso del módulo Clientes.</summary>
public interface ICustomerService
{
    Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken);

    Task<CustomerDetails?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Sugerencias para el selector de propietario: solo clientes activos, como máximo 8.</summary>
    Task<IReadOnlyList<CustomerOption>> SuggestActiveAsync(string? text, CancellationToken cancellationToken);

    /// <summary>Datos básicos de un cliente (activo o no) para mostrarlo como propietario.</summary>
    Task<CustomerOption?> GetOptionAsync(int id, CancellationToken cancellationToken);

    /// <summary>Registra un cliente. Si tiene éxito, devuelve su Id.</summary>
    Task<CommandResult<int>> CreateAsync(CustomerInput input, CancellationToken cancellationToken);

    /// <param name="version">Versión que vio quien edita: si cambió, se devuelve un conflicto sin guardar.</param>
    Task<CommandResult> UpdateAsync(int id, CustomerInput input, int version, CancellationToken cancellationToken);

    Task<CommandResult> DeactivateAsync(int id, CancellationToken cancellationToken);

    Task<CommandResult> ReactivateAsync(int id, CancellationToken cancellationToken);
}
