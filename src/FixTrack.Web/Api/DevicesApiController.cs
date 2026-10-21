using FixTrack.Application.ReadApi;
using FixTrack.Web.Portfolio;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Api;

/// <summary>Equipos: <c>GET /api/v1/equipos</c>.</summary>
[Route(RoutePrefix + "/equipos")]
[PortfolioFeature(PortfolioFeature.ReadOnlyApi)]
public sealed class DevicesApiController(IReadApiService api) : ApiV1Controller
{
    /// <summary>Listado paginado con filtro por tipo y cliente.</summary>
    [HttpGet("")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "pagina")] string? page,
        [FromQuery(Name = "porPagina")] string? pageSize,
        [FromQuery(Name = "tipo")] string? type,
        [FromQuery(Name = "cliente")] string? customer,
        [FromQuery(Name = "orden")] string? sort,
        CancellationToken cancellationToken)
    {
        var parameters = new ApiParameters();
        var query = parameters.Devices(page, pageSize, type, customer, sort);
        if (!parameters.IsValid)
        {
            return Invalid(parameters);
        }

        var result = await api.ListDevicesAsync(query, cancellationToken);
        return Data(new ApiPage<ApiDevice>([.. result.Items.Select(ApiDevice.From)], ApiPagination.From(result, query.PageSize)));
    }
}
