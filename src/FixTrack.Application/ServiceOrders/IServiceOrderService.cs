using FixTrack.Application.Common;

namespace FixTrack.Application.ServiceOrders;

/// <summary>
/// Casos de uso del módulo Órdenes de servicio. Las operaciones sobre una orden existente reciben la
/// <c>version</c> que vio el usuario: si otra persona la modificó entretanto, devuelven un conflicto.
/// </summary>
public interface IServiceOrderService
{
    Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, CancellationToken cancellationToken);

    Task<ServiceOrderDetails?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Órdenes de los equipos de un cliente: cantidades y las 10 más recientes.</summary>
    Task<CustomerServiceOrders> ListByCustomerAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Registra la recepción de un equipo. Si tiene éxito, devuelve el Id de la orden.</summary>
    Task<CommandResult<int>> CreateAsync(ServiceOrderInput input, CancellationToken cancellationToken);

    /// <summary>Asigna o reasigna el técnico responsable (solo técnicos activos).</summary>
    Task<CommandResult> AssignTechnicianAsync(int id, int? technicianId, int version, CancellationToken cancellationToken);

    Task<CommandResult> RecordDiagnosisAsync(int id, string? diagnosis, int version, CancellationToken cancellationToken);

    /// <summary>Actualiza la prioridad, la entrega comprometida y el costo estimado.</summary>
    Task<CommandResult> UpdatePlanAsync(int id, ServiceOrderPlanInput input, int version, CancellationToken cancellationToken);

    /// <summary>Cambia el estado y agrega el registro al historial en la misma operación.</summary>
    Task<CommandResult> ChangeStatusAsync(int id, ServiceOrderStatusInput input, int version, CancellationToken cancellationToken);
}
