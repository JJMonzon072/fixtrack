using FixTrack.Application.Auditing;
using FixTrack.Web.Models.Audit;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Registro de auditoría: quién hizo cada operación importante y cuándo. Solo para administradores.</summary>
[Route("auditoria")]
[Authorize(Policy = AuthorizationPolicies.ManageAccounts)]
public sealed class AuditController(IAuditTrail audit) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "area")] string? area,
        [FromQuery(Name = "pagina")] int page,
        CancellationToken cancellationToken)
    {
        var selected = AuditDisplay.ParseArea(area);
        var result = await audit.SearchAsync(new AuditSearch(selected, page), cancellationToken);
        return View(new AuditIndexViewModel(selected, result));
    }
}
