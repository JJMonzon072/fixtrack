using FixTrack.Application.Common;
using FixTrack.Application.Technicians;
using FixTrack.Web.Models;
using FixTrack.Web.Models.Technicians;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>
/// Módulo Técnicos: listado, búsqueda, registro, edición, desactivación y reactivación. Cualquier rol del taller
/// consulta los técnicos; solo el administrador los registra o modifica.
/// </summary>
[Route("tecnicos")]
[Authorize(Policy = AuthorizationPolicies.Workshop)]
public sealed class TechniciansController(ITechnicianService technicians) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "buscar")] string? search,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "pagina")] int page,
        CancellationToken cancellationToken)
    {
        var filter = StatusFilterQuery.Parse(status);
        var result = await technicians.SearchAsync(new TechnicianSearch(search, filter, page), cancellationToken);
        return View(new TechnicianIndexViewModel(search?.Trim(), filter, result));
    }

    [HttpGet("nuevo")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public IActionResult Create() => View("Editor", new TechnicianEditorViewModel(null, null, new TechnicianFormModel()));

    [HttpPost("nuevo")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public async Task<IActionResult> Create(TechnicianFormModel form, CancellationToken cancellationToken)
    {
        var result = await technicians.CreateAsync(form.ToInput(), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("Editor", new TechnicianEditorViewModel(null, null, form));
        }

        StatusMessage.Success(TempData, "Técnico registrado correctamente.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(id, cancellationToken);
        return technician is null ? NotFound() : View(technician);
    }

    [HttpGet("{id:int}/editar")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(id, cancellationToken);
        return technician is null
            ? NotFound()
            : View("Editor", new TechnicianEditorViewModel(id, technician.FullName, TechnicianFormModel.From(technician)));
    }

    [HttpPost("{id:int}/editar")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public async Task<IActionResult> Edit(int id, TechnicianFormModel form, CancellationToken cancellationToken)
    {
        var result = await technicians.UpdateAsync(id, form.ToInput(), cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrors(result);
            var current = await technicians.GetAsync(id, cancellationToken);
            return View("Editor", new TechnicianEditorViewModel(id, current?.FullName, form));
        }

        StatusMessage.Success(TempData, "Los cambios se guardaron correctamente.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/desactivar")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, technicians.DeactivateAsync, "Técnico desactivado. Su información e historial se conservan.", cancellationToken);

    [HttpPost("{id:int}/reactivar")]
    [Authorize(Policy = AuthorizationPolicies.ManageTechnicians)]
    public Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, technicians.ReactivateAsync, "Técnico reactivado.", cancellationToken);

    private async Task<IActionResult> ChangeStatusAsync(
        int id,
        Func<int, CancellationToken, Task<CommandResult>> change,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var result = await change(id, cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            StatusMessage.Success(TempData, successMessage);
        }
        else
        {
            StatusMessage.Warning(TempData, result.Message ?? "No se pudo completar la operación.");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private void AddErrors(CommandResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }
    }
}
