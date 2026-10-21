using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.Reports;
using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.ReadApi;

/// <summary>Límites de la API de consulta: ninguna página es ilimitada.</summary>
public static class ReadApiLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Página máxima que se acepta (evita desplazamientos enormes en la base).</summary>
    public const int MaxPage = 10_000;
}

/// <summary>Campos por los que se pueden ordenar las órdenes. El Id desempata siempre.</summary>
public enum OrderSortField
{
    Received,
    Promised,
    Priority,
    Number,
}

/// <summary>Consulta paginada de órdenes para la API.</summary>
/// <param name="ReceivedFromUtc">Recibidas desde este instante (incluido).</param>
/// <param name="ReceivedToUtc">Recibidas antes de este instante (excluido).</param>
public sealed record OrderListQuery(
    int Page = 1,
    int PageSize = ReadApiLimits.DefaultPageSize,
    ServiceOrderStatus? Status = null,
    ServiceOrderPriority? Priority = null,
    int? TechnicianId = null,
    bool UnassignedOnly = false,
    int? DeviceId = null,
    int? CustomerId = null,
    DateTime? ReceivedFromUtc = null,
    DateTime? ReceivedToUtc = null,
    OrderSortField Sort = OrderSortField.Received,
    bool Descending = true);

public enum DeviceSortField
{
    Registered,
    Brand,
}

/// <summary>Consulta paginada de equipos para la API.</summary>
public sealed record DeviceListQuery(
    int Page = 1,
    int PageSize = ReadApiLimits.DefaultPageSize,
    DeviceType? Type = null,
    int? CustomerId = null,
    DeviceSortField Sort = DeviceSortField.Registered,
    bool Descending = true);

/// <summary>Consultas de solo lectura de la API, implementadas con proyecciones en SQL (sin entidades de EF Core).</summary>
public interface IReadApiQueries
{
    Task<PagedResult<ServiceOrderListItem>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken);

    Task<PagedResult<DeviceListItem>> ListDevicesAsync(DeviceListQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Casos de uso de la API REST de solo lectura. Reutiliza los de la aplicación (detalle de una orden y reportes) para
/// no duplicar reglas; solo agrega los listados paginados con orden y tamaño de página controlados.
/// </summary>
public interface IReadApiService
{
    Task<PagedResult<ServiceOrderListItem>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken);

    Task<ServiceOrderDetails?> GetOrderAsync(int id, CancellationToken cancellationToken);

    Task<PagedResult<DeviceListItem>> ListDevicesAsync(DeviceListQuery query, CancellationToken cancellationToken);

    Task<OperationsReport> GetMetricsAsync(ReportFilter filter, CancellationToken cancellationToken);
}

internal sealed class ReadApiService(IReadApiQueries queries, IServiceOrderService orders, IReportService reports) : IReadApiService
{
    public Task<PagedResult<ServiceOrderListItem>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken)
    {
        EnsurePage(query.Page, query.PageSize);
        return queries.ListOrdersAsync(query, cancellationToken);
    }

    public Task<ServiceOrderDetails?> GetOrderAsync(int id, CancellationToken cancellationToken) => orders.GetAsync(id, cancellationToken);

    public Task<PagedResult<DeviceListItem>> ListDevicesAsync(DeviceListQuery query, CancellationToken cancellationToken)
    {
        EnsurePage(query.Page, query.PageSize);
        return queries.ListDevicesAsync(query, cancellationToken);
    }

    public Task<OperationsReport> GetMetricsAsync(ReportFilter filter, CancellationToken cancellationToken) =>
        reports.GetAsync(filter, cancellationToken);

    /// <summary>La presentación valida los parámetros; esta comprobación evita consultas sin límite si alguien la omite.</summary>
    private static void EnsurePage(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, ReadApiLimits.MaxPage);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, ReadApiLimits.MaxPageSize);
    }
}
