using FixTrack.Application.ServiceOrders;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Cola de trabajo: órdenes abiertas ordenadas por el algoritmo de priorización.</summary>
[Route("cola-de-trabajo")]
[Authorize(Policy = AuthorizationPolicies.Workshop)]
public sealed class WorkQueueController(IServiceOrderService orders) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await orders.GetWorkQueueAsync(cancellationToken));
}
