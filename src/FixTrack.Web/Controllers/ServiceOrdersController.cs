using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Models;
using FixTrack.Web.Models.ServiceOrders;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Módulo Órdenes de servicio: listado, registro y detalle con historial.</summary>
[Route("ordenes")]
public sealed class ServiceOrdersController(
    IServiceOrderService orders,
    ICustomerService customers,
    IDeviceService devices,
    ITechnicianService technicians,
    DateDisplay dates,
    TimeProvider clock) : Controller
{
    private const string InvalidDateMessage = "Escribe una fecha y hora válidas.";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "buscar")] string? search,
        [FromQuery(Name = "vista")] string? view,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "prioridad")] string? priority,
        [FromQuery(Name = "tecnico")] string? technician,
        [FromQuery(Name = "cliente")] int? customer,
        [FromQuery(Name = "equipo")] int? device,
        [FromQuery(Name = "pagina")] int page,
        CancellationToken cancellationToken)
    {
        var query = ServiceOrderListQuery.Parse(search, view, status, priority, technician, customer, device);
        var result = await orders.SearchAsync(query.ToSearch(page), cancellationToken);
        var workload = await technicians.ListWorkloadAsync(cancellationToken);
        return View(new ServiceOrderIndexViewModel(query, result, workload, UtcNow));
    }

    /// <param name="customerId">Cliente preseleccionado (desde su detalle).</param>
    /// <param name="deviceId">Equipo preseleccionado (desde su detalle); implica a su propietario.</param>
    [HttpGet("nueva")]
    public async Task<IActionResult> Create(
        [FromQuery(Name = "cliente")] int? customerId,
        [FromQuery(Name = "equipo")] int? deviceId,
        CancellationToken cancellationToken)
    {
        var form = new ServiceOrderFormModel { Priority = ServiceOrderPriority.Normal };
        CustomerOption? customer = null;
        if (deviceId is > 0 && await devices.GetAsync(deviceId.Value, cancellationToken) is { } device)
        {
            customer = device.Owner;
            form.DeviceId = device.Id;
            form.DeviceLocked = true;
        }
        else if (customerId is > 0)
        {
            customer = await customers.GetOptionAsync(customerId.Value, cancellationToken);
        }

        string? notice = null;
        if (customer is { IsActive: false })
        {
            notice = $"{customer.FullName} está inactivo y no admite órdenes nuevas. Reactívalo o elige otro cliente.";
            customer = null;
            form.DeviceId = null;
            form.DeviceLocked = false;
        }

        form.CustomerId = customer?.Id;
        return View("Editor", await EditorAsync(form, customer, notice, cancellationToken));
    }

    [HttpPost("nueva")]
    public async Task<IActionResult> Create(ServiceOrderFormModel form, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var promisedAt = ParseDate(form.PromisedAt, nameof(ServiceOrder.PromisedAtUtc), errors);
        var estimatedCost = ParseMoney(form.EstimatedCost, nameof(ServiceOrder.EstimatedCost), errors);

        if (errors.Count == 0)
        {
            var result = await orders.CreateAsync(
                new ServiceOrderInput(
                    form.CustomerId,
                    form.DeviceId,
                    form.ReportedIssue,
                    form.Priority,
                    form.TechnicianId,
                    promisedAt,
                    estimatedCost,
                    form.IntakeNotes),
                cancellationToken);
            if (result.Succeeded)
            {
                StatusMessage.Success(TempData, $"Orden {ServiceOrder.FormatNumber(result.Value)} registrada correctamente.");
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }

            errors = new Dictionary<string, string>(result.Errors);
        }

        AddErrors(errors);
        var customer = form.CustomerId is > 0 ? await customers.GetOptionAsync(form.CustomerId.Value, cancellationToken) : null;
        return View("Editor", await EditorAsync(form, customer, null, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        return order is null ? NotFound() : View(new ServiceOrderDetailsViewModel(order, UtcNow));
    }

    private async Task<ServiceOrderEditorViewModel> EditorAsync(
        ServiceOrderFormModel form,
        CustomerOption? customer,
        string? notice,
        CancellationToken cancellationToken)
    {
        var customerDevices = customer is null ? [] : await devices.ListByCustomerAsync(customer.Id, cancellationToken);
        return new ServiceOrderEditorViewModel(form, customer, customerDevices, await ActiveTechniciansAsync(cancellationToken), notice);
    }

    private async Task<IReadOnlyList<TechnicianWorkload>> ActiveTechniciansAsync(CancellationToken cancellationToken) =>
        [.. (await technicians.ListWorkloadAsync(cancellationToken)).Where(technician => technician.IsActive)];

    private DateTime? ParseDate(string? value, string field, Dictionary<string, string> errors)
    {
        if (dates.TryParseInput(value, out var utc))
        {
            return utc;
        }

        errors[field] = InvalidDateMessage;
        return null;
    }

    private static decimal? ParseMoney(string? value, string field, Dictionary<string, string> errors)
    {
        if (Money.TryParse(value, out var amount))
        {
            return amount;
        }

        errors[field] = Money.InvalidMessage;
        return null;
    }

    private void AddErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (field, message) in errors)
        {
            ModelState.AddModelError(field, message);
        }
    }
}
