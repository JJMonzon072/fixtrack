using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Web.Models;
using FixTrack.Web.Models.Devices;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Módulo Equipos: listado, búsqueda, registro, detalle y edición.</summary>
[Route("equipos")]
public sealed class DevicesController(IDeviceService devices, ICustomerService customers) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "buscar")] string? search,
        [FromQuery(Name = "tipo")] string? type,
        [FromQuery(Name = "pagina")] int page,
        CancellationToken cancellationToken)
    {
        var deviceType = DomainDisplay.ParseDeviceType(type);
        var result = await devices.SearchAsync(new DeviceSearch(search, deviceType, page), cancellationToken);
        return View(new DeviceIndexViewModel(search?.Trim(), deviceType, result));
    }

    /// <param name="customerId">Cliente preseleccionado cuando el registro se inicia desde su detalle.</param>
    [HttpGet("nuevo")]
    public async Task<IActionResult> Create([FromQuery(Name = "cliente")] int? customerId, CancellationToken cancellationToken)
    {
        CustomerOption? owner = null;
        string? notice = null;
        if (customerId is > 0 && await customers.GetOptionAsync(customerId.Value, cancellationToken) is { } option)
        {
            if (option.IsActive)
            {
                owner = option;
            }
            else
            {
                notice = $"{option.FullName} está inactivo y no admite equipos nuevos. Reactívalo o elige otro cliente.";
            }
        }

        return View("Editor", new DeviceEditorViewModel(null, null, new DeviceFormModel { CustomerId = owner?.Id }, owner, notice));
    }

    [HttpPost("nuevo")]
    public async Task<IActionResult> Create(DeviceFormModel form, CancellationToken cancellationToken)
    {
        var result = await devices.RegisterAsync(new RegisterDeviceInput(form.CustomerId, form.ToInput()), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            var owner = form.CustomerId is > 0 ? await customers.GetOptionAsync(form.CustomerId.Value, cancellationToken) : null;
            return View("Editor", new DeviceEditorViewModel(null, null, form, owner));
        }

        StatusMessage.Success(TempData, "Equipo registrado correctamente.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        return device is null ? NotFound() : View(device);
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        return device is null
            ? NotFound()
            : View("Editor", new DeviceEditorViewModel(id, DomainDisplay.DeviceTitle(device.Brand, device.Model), DeviceFormModel.From(device), device.Owner));
    }

    /// <remarks>El propietario enviado en el formulario se ignora: un equipo no cambia de dueño.</remarks>
    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Edit(int id, DeviceFormModel form, CancellationToken cancellationToken)
    {
        var result = await devices.UpdateAsync(id, form.ToInput(), cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrors(result);
            var current = await devices.GetAsync(id, cancellationToken);
            return View("Editor", new DeviceEditorViewModel(
                id,
                current is null ? null : DomainDisplay.DeviceTitle(current.Brand, current.Model),
                form,
                current?.Owner));
        }

        StatusMessage.Success(TempData, "Los cambios se guardaron correctamente.");
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
