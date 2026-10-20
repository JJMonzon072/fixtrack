using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.ServiceOrders;

/// <summary>Acceso a las órdenes de servicio. Lo implementa la capa de infraestructura.</summary>
public interface IServiceOrderRepository
{
    /// <param name="nowUtc">Momento de referencia para decidir qué órdenes están atrasadas.</param>
    Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, DateTime nowUtc, CancellationToken cancellationToken);

    Task<ServiceOrderDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Cantidad total y abierta de órdenes de los equipos de un cliente y las <paramref name="take"/> más recientes.</summary>
    Task<CustomerServiceOrders> ListByCustomerAsync(int customerId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Órdenes abiertas para priorizarlas, como máximo <paramref name="limit"/>, empezando por las recibidas hace más
    /// tiempo. Las órdenes abiertas son el trabajo en curso del taller, así que su cantidad es acotada.
    /// </summary>
    Task<IReadOnlyList<OpenOrderSnapshot>> ListOpenAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene la orden para modificarla. Su historial no se carga: los cambios de estado solo agregan registros.
    /// </summary>
    Task<ServiceOrder?> FindAsync(int id, CancellationToken cancellationToken);

    /// <summary>Id de la orden registrada con ese envío del formulario, si existe.</summary>
    Task<int?> FindIdByCreationRequestAsync(Guid requestId, CancellationToken cancellationToken);

    void Add(ServiceOrder order);
}
