using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Equipos. Módulo pendiente de implementación: muestra su página provisional.</summary>
[Route("equipos")]
public sealed class DevicesController(IModuleCatalog modules) : Controller
{
    [HttpGet("")]
    public IActionResult Index() =>
        View("ModulePlaceholder", new ModulePlaceholderViewModel(
            Module: modules.Get(ModuleKey.Devices),
            EmptyTitle: "Todavía no hay equipos registrados",
            CreateLabel: "Registrar equipo",
            Columns: ["Equipo", "Tipo", "Marca y modelo", "Número de serie", "Propietario"]));
}
