using FixTrack.Domain.Technicians;

namespace FixTrack.Application.Technicians;

/// <summary>Acceso a los técnicos. Lo implementa la capa de infraestructura.</summary>
public interface ITechnicianRepository
{
    Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search, CancellationToken cancellationToken);

    Task<TechnicianDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Obtiene el técnico para modificarlo.</summary>
    Task<Technician?> FindAsync(int id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, int? excludingTechnicianId, CancellationToken cancellationToken);

    /// <summary>Órdenes asignadas al técnico que no están entregadas ni canceladas.</summary>
    Task<int> CountOpenOrdersAsync(int technicianId, CancellationToken cancellationToken);

    void Add(Technician technician);
}
