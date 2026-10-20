using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Models;
using FixTrack.Web.Models.ServiceOrders;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>
/// Módulo Órdenes de servicio: listado, registro, detalle con historial y las acciones sobre una orden abierta
/// (asignar técnico, diagnóstico, planificación y cambio de estado). Cada acción tiene su propia página para que
/// los errores de validación se muestren junto al formulario sin perder lo escrito.
/// </summary>
[Route("ordenes")]
[Authorize(Policy = AuthorizationPolicies.Workshop)]
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

    /// <param name="next">Estado sugerido (por ejemplo, desde un botón del detalle).</param>
    [HttpGet("{id:int}/estado")]
    public Task<IActionResult> ChangeStatus(int id, [FromQuery(Name = "a")] string? next, CancellationToken cancellationToken) =>
        WithOpenOrderAsync(id, cancellationToken, order =>
        {
            var suggested = DomainDisplay.ParseStatus(next);
            var form = new ServiceOrderStatusFormModel
            {
                Status = order.NextSteps.Any(step => step.Status == suggested && step.IsAvailable) ? suggested : null,
                FinalCost = Money.InputValue(order.EstimatedCost),
                Version = order.Version,
            };
            return View("Status", new ServiceOrderStatusViewModel(order, form));
        });

    [HttpPost("{id:int}/estado")]
    public async Task<IActionResult> ChangeStatus(int id, ServiceOrderStatusFormModel form, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var finalCost = form.Status == ServiceOrderStatus.Delivered
            ? ParseMoney(form.FinalCost, nameof(ServiceOrder.FinalCost), errors)
            : null;

        if (errors.Count == 0)
        {
            var result = await orders.ChangeStatusAsync(id, new ServiceOrderStatusInput(form.Status, form.Note, finalCost), form.Version, cancellationToken);
            if (Completed(result, id, $"La orden pasó a «{(form.Status is { } status ? DomainDisplay.Name(status) : string.Empty)}».") is { } done)
            {
                return done;
            }

            errors = new Dictionary<string, string>(result.Errors);
        }

        AddErrors(errors);
        return await WithOpenOrderAsync(id, cancellationToken, order =>
            View("Status", new ServiceOrderStatusViewModel(order, form)));
    }

    [HttpGet("{id:int}/asignar")]
    public Task<IActionResult> Assign(int id, CancellationToken cancellationToken) =>
        WithOpenOrderAsync(id, cancellationToken, async order =>
            View("Assign", new ServiceOrderAssignViewModel(
                order, await ActiveTechniciansAsync(cancellationToken), null, order.Version)));

    [HttpPost("{id:int}/asignar")]
    public async Task<IActionResult> Assign(
        int id,
        [FromForm] int? technicianId,
        [FromForm] string? note,
        [FromForm] int version,
        CancellationToken cancellationToken)
    {
        var result = await orders.AssignTechnicianAsync(id, technicianId, note, version, cancellationToken);
        if (Completed(result, id, "Técnico asignado correctamente.") is { } done)
        {
            return done;
        }

        AddErrors(result.Errors);
        return await WithOpenOrderAsync(id, cancellationToken, async order =>
            View("Assign", new ServiceOrderAssignViewModel(order, await ActiveTechniciansAsync(cancellationToken), technicianId, version, note)));
    }

    [HttpGet("{id:int}/diagnostico")]
    public Task<IActionResult> Diagnosis(int id, CancellationToken cancellationToken) =>
        WithOpenOrderAsync(id, cancellationToken, order =>
        {
            if (!order.CanRecordDiagnosis)
            {
                StatusMessage.Warning(TempData, "El diagnóstico se registra mientras la orden está en diagnóstico, en reparación o en espera de repuestos.");
                return RedirectToAction(nameof(Details), new { id });
            }

            return View("Diagnosis", new ServiceOrderDiagnosisViewModel(order, order.Diagnosis, order.Version));
        });

    [HttpPost("{id:int}/diagnostico")]
    public async Task<IActionResult> Diagnosis(
        int id,
        [FromForm] string? diagnosis,
        [FromForm] int version,
        CancellationToken cancellationToken)
    {
        var result = await orders.RecordDiagnosisAsync(id, diagnosis, version, cancellationToken);
        if (Completed(result, id, "Diagnóstico guardado.") is { } done)
        {
            return done;
        }

        AddErrors(result.Errors);
        return await WithOpenOrderAsync(id, cancellationToken, order =>
            View("Diagnosis", new ServiceOrderDiagnosisViewModel(order, diagnosis, version)));
    }

    [HttpGet("{id:int}/planificacion")]
    public Task<IActionResult> Plan(int id, CancellationToken cancellationToken) =>
        WithOpenOrderAsync(id, cancellationToken, order =>
            View("Plan", new ServiceOrderPlanViewModel(order, new ServiceOrderPlanFormModel
            {
                Priority = order.Priority,
                PromisedAt = dates.InputValue(order.PromisedAtUtc),
                EstimatedCost = Money.InputValue(order.EstimatedCost),
                Version = order.Version,
            })));

    [HttpPost("{id:int}/planificacion")]
    public async Task<IActionResult> Plan(int id, ServiceOrderPlanFormModel form, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var promisedAt = ParseDate(form.PromisedAt, nameof(ServiceOrder.PromisedAtUtc), errors);
        var estimatedCost = ParseMoney(form.EstimatedCost, nameof(ServiceOrder.EstimatedCost), errors);

        if (errors.Count == 0)
        {
            var result = await orders.UpdatePlanAsync(id, new ServiceOrderPlanInput(form.Priority, promisedAt, estimatedCost), form.Version, cancellationToken);
            if (Completed(result, id, "Planificación actualizada.") is { } done)
            {
                return done;
            }

            errors = new Dictionary<string, string>(result.Errors);
        }

        AddErrors(errors);
        return await WithOpenOrderAsync(id, cancellationToken, order => View("Plan", new ServiceOrderPlanViewModel(order, form)));
    }

    /// <summary>
    /// Resultado de una acción que terminó: éxito (vuelve al detalle con el mensaje), orden inexistente (404) o
    /// conflicto (vuelve al detalle con el aviso para que se vean los datos actuales). Si los datos no son válidos,
    /// devuelve <c>null</c> y la acción vuelve a mostrar su formulario.
    /// </summary>
    private IActionResult? Completed(CommandResult result, int id, string successMessage)
    {
        switch (result.Status)
        {
            case CommandStatus.Succeeded:
                StatusMessage.Success(TempData, successMessage);
                return RedirectToAction(nameof(Details), new { id });
            case CommandStatus.NotFound:
                return NotFound();
            case CommandStatus.Conflict:
                StatusMessage.Warning(TempData, result.Message ?? "No se pudo completar la operación.");
                return RedirectToAction(nameof(Details), new { id });
            default:
                return null;
        }
    }

    private Task<IActionResult> WithOpenOrderAsync(int id, CancellationToken cancellationToken, Func<ServiceOrderDetails, IActionResult> render) =>
        WithOpenOrderAsync(id, cancellationToken, order => Task.FromResult(render(order)));

    /// <summary>Carga la orden y comprueba que siga abierta antes de mostrar uno de sus formularios.</summary>
    private async Task<IActionResult> WithOpenOrderAsync(
        int id,
        CancellationToken cancellationToken,
        Func<ServiceOrderDetails, Task<IActionResult>> render)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.IsClosed)
        {
            StatusMessage.Warning(TempData, "La orden está cerrada (entregada o cancelada) y ya no admite cambios.");
            return RedirectToAction(nameof(Details), new { id });
        }

        return await render(order);
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
