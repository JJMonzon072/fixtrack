using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Clientes. Módulo pendiente de implementación: muestra su página provisional.</summary>
[Route("clientes")]
public sealed class CustomersController(IModuleCatalog modules) : Controller
{
    [HttpGet("")]
    public IActionResult Index() =>
        View("ModulePlaceholder", new ModulePlaceholderViewModel(
            Module: modules.Get(ModuleKey.Customers),
            EmptyTitle: "Todavía no hay clientes registrados",
            CreateLabel: "Nuevo cliente",
            Columns: ["Cliente", "Correo electrónico", "Teléfono", "Equipos", "Última orden"]));
}
