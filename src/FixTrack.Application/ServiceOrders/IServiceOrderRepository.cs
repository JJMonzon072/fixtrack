using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.ServiceOrders;

/// <summary>Acceso a las órdenes de servicio. Lo implementa la capa de infraestructura.</summary>
public interface IServiceOrderRepository
{
    /// <param name="nowUtc">Momento de referencia para decidir qué órdenes están atrasadas.</param>
    Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, DateTime nowUtc, CancellationToken cancellationToken);

    Task<ServiceOrderDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene la orden para modificarla. Su historial no se carga: los cambios de estado solo agregan registros.
    /// </summary>
    Task<ServiceOrder?> FindAsync(int id, CancellationToken cancellationToken);

    void Add(ServiceOrder order);
}
