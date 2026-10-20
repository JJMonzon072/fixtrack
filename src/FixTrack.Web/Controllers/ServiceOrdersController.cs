using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Órdenes de servicio. Módulo pendiente de implementación: muestra su página provisional.</summary>
[Route("ordenes")]
public sealed class ServiceOrdersController(IModuleCatalog modules) : Controller
{
    [HttpGet("")]
    public IActionResult Index() =>
        View("ModulePlaceholder", new ModulePlaceholderViewModel(
            Module: modules.Get(ModuleKey.ServiceOrders),
            EmptyTitle: "Todavía no hay órdenes de servicio",
            CreateLabel: "Nueva orden",
            Columns: ["Orden", "Cliente", "Equipo", "Técnico", "Estado", "Prioridad", "Entrega estimada"]));
}
