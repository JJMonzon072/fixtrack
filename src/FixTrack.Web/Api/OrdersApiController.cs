using FixTrack.Application.ReadApi;
using FixTrack.Web.Portfolio;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Api;

/// <summary>Órdenes de servicio: <c>GET /api/v1/ordenes</c> y <c>GET /api/v1/ordenes/{id}</c>.</summary>
[Route(RoutePrefix + "/ordenes")]
[PortfolioFeature(PortfolioFeature.ReadOnlyApi)]
public sealed class OrdersApiController(IReadApiService api, TimeProvider clock) : ApiV1Controller
{
    /// <summary>Listado paginado con filtros y orden controlado.</summary>
    [HttpGet("")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "pagina")] string? page,
        [FromQuery(Name = "porPagina")] string? pageSize,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "prioridad")] string? priority,
        [FromQuery(Name = "tecnico")] string? technician,
        [FromQuery(Name = "equipo")] string? device,
        [FromQuery(Name = "cliente")] string? customer,
        [FromQuery(Name = "recibidaDesde")] string? receivedFrom,
        [FromQuery(Name = "recibidaHasta")] string? receivedTo,
        [FromQuery(Name = "orden")] string? sort,
        CancellationToken cancellationToken)
    {
        var parameters = new ApiParameters();
        var query = parameters.Orders(page, pageSize, status, priority, technician, device, customer, receivedFrom, receivedTo, sort, clock.LocalTimeZone);
        if (!parameters.IsValid)
        {
            return Invalid(parameters);
        }

        var result = await api.ListOrdersAsync(query, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        return Data(new ApiPage<ApiOrderSummary>(
            [.. result.Items.Select(order => ApiOrderSummary.From(order, now))],
            ApiPagination.From(result, query.PageSize)));
    }

    /// <summary>Detalle de una orden con su historial de estados (sin autores ni notas).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var order = await api.GetOrderAsync(id, cancellationToken);
        return order is null
            ? Problem(StatusCodes.Status404NotFound, "Orden no encontrada", $"No existe una orden con el Id {id}.")
            : Data(ApiOrderDetail.From(order, clock.GetUtcNow().UtcDateTime));
    }
}
