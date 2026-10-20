using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Técnicos. Módulo pendiente de implementación: muestra su página provisional.</summary>
[Route("tecnicos")]
public sealed class TechniciansController(IModuleCatalog modules) : Controller
{
    [HttpGet("")]
    public IActionResult Index() =>
        View("ModulePlaceholder", new ModulePlaceholderViewModel(
            Module: modules.Get(ModuleKey.Technicians),
            EmptyTitle: "Todavía no hay técnicos registrados",
            CreateLabel: "Nuevo técnico",
            Columns: ["Técnico", "Especialidad", "Correo electrónico", "Órdenes activas"]));
}
