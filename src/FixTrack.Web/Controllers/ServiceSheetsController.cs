using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Portfolio;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>
/// Fichas de servicio imprimibles (extra de portafolio, <c>PortfolioFeatures:PrintableSheets</c>): ficha de recepción y
/// comprobante de entrega de una orden, en formato A4 para imprimir o guardar como PDF con el navegador. Exigen sesión y
/// los permisos del taller, como el detalle de la orden: no hay enlaces públicos ni archivos guardados en el servidor.
/// </summary>
[Route("fichas")]
[Authorize(Policy = AuthorizationPolicies.Workshop)]
[PortfolioFeature(PortfolioFeature.PrintableSheets)]
public sealed class ServiceSheetsController(IServiceOrderService orders, TimeProvider clock) : Controller
{
    [HttpGet("recepcion/{id:int}")]
    public async Task<IActionResult> Reception(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        return order is null ? NotFound() : View(new ServiceSheetViewModel(order, clock.GetUtcNow().UtcDateTime));
    }

    /// <summary>Solo existe para las órdenes entregadas; para las demás se explica por qué (409).</summary>
    [HttpGet("entrega/{id:int}")]
    public async Task<IActionResult> Delivery(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var model = new ServiceSheetViewModel(order, clock.GetUtcNow().UtcDateTime);
        if (order.Status != ServiceOrderStatus.Delivered)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return View("NotDelivered", model);
        }

        return View(model);
    }
}

/// <param name="IssuedAtUtc">Momento en que se generó la ficha.</param>
public sealed record ServiceSheetViewModel(ServiceOrderDetails Order, DateTime IssuedAtUtc);
