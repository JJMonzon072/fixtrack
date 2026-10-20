using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Web.Models;
using FixTrack.Web.Models.Customers;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Módulo Clientes: listado, búsqueda, registro, edición, desactivación y reactivación.</summary>
[Route("clientes")]
public sealed class CustomersController(ICustomerService customers, IDeviceService devices) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "buscar")] string? search,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "pagina")] int page,
        CancellationToken cancellationToken)
    {
        var filter = StatusFilterQuery.Parse(status);
        var result = await customers.SearchAsync(new CustomerSearch(search, filter, page), cancellationToken);
        return View(new CustomerIndexViewModel(search?.Trim(), filter, result));
    }

    /// <summary>Sugerencias de clientes activos para el selector de propietario del formulario de equipos.</summary>
    [HttpGet("opciones")]
    public async Task<IActionResult> Options([FromQuery(Name = "buscar")] string? search, CancellationToken cancellationToken)
    {
        var options = await customers.SuggestActiveAsync(search, cancellationToken);
        return Json(options.Select(option => new { option.Id, option.FullName, option.Phone, option.Email }));
    }

    /// <summary>Equipos del cliente para el selector del formulario de órdenes.</summary>
    [HttpGet("{id:int}/equipos")]
    public async Task<IActionResult> Devices(int id, CancellationToken cancellationToken)
    {
        if (await customers.GetOptionAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        var customerDevices = await devices.ListByCustomerAsync(id, cancellationToken);
        return Json(customerDevices.Select(device => new
        {
            device.Id,
            Title = DomainDisplay.DeviceTitle(device.Brand, device.Model),
            Type = DomainDisplay.Name(device.Type),
            device.SerialNumber,
        }));
    }

    [HttpGet("nuevo")]
    public IActionResult Create() => View("Editor", new CustomerEditorViewModel(null, null, new CustomerFormModel()));

    [HttpPost("nuevo")]
    public async Task<IActionResult> Create(CustomerFormModel form, CancellationToken cancellationToken)
    {
        var result = await customers.CreateAsync(form.ToInput(), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("Editor", new CustomerEditorViewModel(null, null, form));
        }

        StatusMessage.Success(TempData, "Cliente registrado correctamente.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        var customerDevices = await devices.ListByCustomerAsync(id, cancellationToken);
        return View(new CustomerDetailsViewModel(customer, customerDevices));
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(id, cancellationToken);
        return customer is null
            ? NotFound()
            : View("Editor", new CustomerEditorViewModel(id, customer.FullName, CustomerFormModel.From(customer)));
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Edit(int id, CustomerFormModel form, CancellationToken cancellationToken)
    {
        var result = await customers.UpdateAsync(id, form.ToInput(), cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrors(result);
            var current = await customers.GetAsync(id, cancellationToken);
            return View("Editor", new CustomerEditorViewModel(id, current?.FullName, form));
        }

        StatusMessage.Success(TempData, "Los cambios se guardaron correctamente.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/desactivar")]
    public Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, customers.DeactivateAsync, "Cliente desactivado. Su información e historial se conservan.", cancellationToken);

    [HttpPost("{id:int}/reactivar")]
    public Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, customers.ReactivateAsync, "Cliente reactivado.", cancellationToken);

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
