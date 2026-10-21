using FixTrack.Application.ReadApi;
using FixTrack.Application.Reports;
using FixTrack.Application.Technicians;
using FixTrack.Web.Portfolio;
using FixTrack.Web.Reports;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Api;

/// <summary>Métricas de un período: <c>GET /api/v1/metricas</c>, con los mismos filtros del centro de reportes.</summary>
[Route(RoutePrefix + "/metricas")]
[PortfolioFeature(PortfolioFeature.ReadOnlyApi)]
public sealed class MetricsApiController(IReadApiService api, ITechnicianService technicians, TimeProvider clock) : ApiV1Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Get(
        [FromQuery(Name = "rango")] string? range,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "prioridad")] string? priority,
        [FromQuery(Name = "tecnico")] string? technician,
        CancellationToken cancellationToken)
    {
        var workload = await technicians.ListWorkloadAsync(cancellationToken);
        var filter = new ReportQuery(range, from, to, status, priority, technician)
            .TryCreateFilter(ReportPeriod.Today(clock), clock.LocalTimeZone, id => workload.Any(item => item.Id == id), out var errors);
        if (filter is null)
        {
            var parameters = new ApiParameters();
            parameters.AddAll(errors);
            return Invalid(parameters);
        }

        return Data(ApiMetrics.From(await api.GetMetricsAsync(filter, cancellationToken)));
    }
}
