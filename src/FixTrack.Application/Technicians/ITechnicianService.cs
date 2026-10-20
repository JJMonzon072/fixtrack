using FixTrack.Application.Common;

namespace FixTrack.Application.Technicians;

/// <summary>Casos de uso del módulo Técnicos.</summary>
public interface ITechnicianService
{
    Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search, CancellationToken cancellationToken);

    Task<TechnicianDetails?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Técnicos con su cantidad de órdenes abiertas (activos e inactivos), ordenados por nombre.</summary>
    Task<IReadOnlyList<TechnicianWorkload>> ListWorkloadAsync(CancellationToken cancellationToken);

    /// <summary>Registra un técnico activo. Si tiene éxito, devuelve su Id.</summary>
    Task<CommandResult<int>> CreateAsync(TechnicianInput input, CancellationToken cancellationToken);

    /// <param name="version">Versión que vio quien edita: si cambió, se devuelve un conflicto sin guardar.</param>
    Task<CommandResult> UpdateAsync(int id, TechnicianInput input, int version, CancellationToken cancellationToken);

    /// <summary>
    /// Desactiva al técnico si no tiene órdenes abiertas asignadas. La comprobación y la desactivación se hacen
    /// con el técnico bloqueado, de modo que ninguna asignación simultánea puede colarse entre ambas.
    /// </summary>
    Task<CommandResult> DeactivateAsync(int id, CancellationToken cancellationToken);

    Task<CommandResult> ReactivateAsync(int id, CancellationToken cancellationToken);
}
