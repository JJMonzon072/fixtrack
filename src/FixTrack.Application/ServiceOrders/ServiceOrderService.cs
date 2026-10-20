using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Common;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;

namespace FixTrack.Application.ServiceOrders;

internal sealed class ServiceOrderService(
    IServiceOrderRepository orders,
    IDeviceRepository devices,
    ITechnicianRepository technicians,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock) : IServiceOrderService
{
    internal const string CustomerField = "CustomerId";
    internal const string DeviceField = "DeviceId";
    internal const string TechnicianField = "TechnicianId";
    internal const string AssignmentNoteField = "Note";

    internal const string StaleMessage =
        "Otra persona modificó esta orden mientras la revisabas. Revisa los datos actuales y vuelve a intentarlo.";

    internal const string ClosedMessage = "La orden está cerrada (entregada o cancelada) y ya no admite cambios.";
    internal const int RecentCustomerOrders = 10;

    /// <summary>Máximo de órdenes abiertas que se priorizan en una consulta (el trabajo en curso de un taller).</summary>
    internal const int MaxPrioritizedOrders = 500;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, CancellationToken cancellationToken)
    {
        var text = Text.CleanOrNull(search.Text);
        var view = Enum.IsDefined(search.View) ? search.View : ServiceOrderView.Open;
        var normalized = search with
        {
            Text = text is { Length: > ServiceOrderSearch.TextMaxLength } ? text[..ServiceOrderSearch.TextMaxLength] : text,
            View = view,
            Status = search.Status is { } status && Enum.IsDefined(status) && BelongsTo(view, status) ? status : null,
            Priority = search.Priority is { } priority && Enum.IsDefined(priority) ? priority : null,
            TechnicianId = search.UnassignedOnly || search.TechnicianId is not > 0 ? null : search.TechnicianId,
            Page = Math.Max(1, search.Page),
            CustomerId = search.CustomerId is > 0 ? search.CustomerId : null,
            DeviceId = search.DeviceId is > 0 ? search.DeviceId : null,
        };

        return orders.SearchAsync(normalized, UtcNow, cancellationToken);
    }

    public Task<ServiceOrderDetails?> GetAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? orders.GetDetailsAsync(id, cancellationToken) : Task.FromResult<ServiceOrderDetails?>(null);

    public Task<CustomerServiceOrders> ListByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        orders.ListByCustomerAsync(customerId, RecentCustomerOrders, cancellationToken);

    public async Task<WorkQueueResult> GetWorkQueueAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var open = await orders.ListOpenAsync(MaxPrioritizedOrders + 1, cancellationToken);
        var scored = open
            .Take(MaxPrioritizedOrders)
            .Select(order => (Order: order, Score: WorkPrioritizer.Score(
                order.Priority, order.Status, order.ReceivedAtUtc, order.PromisedAtUtc, order.TechnicianId is not null, now)))
            .ToList();

        IReadOnlyList<PrioritizedOrder> Ranked(WorkQueue queue) =>
        [
            .. WorkPrioritizer
                .Rank(
                    scored.Where(item => item.Score.Queue == queue),
                    item => item.Score.Total,
                    item => item.Order.PromisedAtUtc,
                    item => item.Order.ReceivedAtUtc,
                    item => item.Order.Id)
                .Select((item, index) => new PrioritizedOrder(item.Order, item.Score, index + 1)),
        ];

        return new WorkQueueResult(
            Ranked(WorkQueue.Repair),
            Ranked(WorkQueue.AwaitingParts),
            Ranked(WorkQueue.ReadyForPickup),
            now,
            open.Count > MaxPrioritizedOrders);
    }

    public Task<CommandResult<int>> CreateAsync(ServiceOrderInput input, CancellationToken cancellationToken)
    {
        var receivedAt = UtcNow;
        var issue = Text.CleanMultilineOrNull(input.ReportedIssue);
        var notes = Text.CleanMultilineOrNull(input.IntakeNotes);
        var fieldErrors = new Dictionary<string, string>(ServiceOrderRules.ValidateIntake(issue, notes));
        foreach (var (field, message) in ServiceOrderRules.ValidatePlan(input.Priority, receivedAt, input.PromisedAtUtc, input.EstimatedCost))
        {
            fieldErrors[field] = message;
        }

        // La transacción mantiene bloqueado al técnico elegido hasta guardar la orden (ver FindForAssignmentAsync).
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var errors = new Dictionary<string, string>(fieldErrors);
                var device = input.DeviceId is > 0 ? await devices.FindWithOwnerAsync(input.DeviceId.Value, token) : null;
                if (input.CustomerId is not > 0)
                {
                    errors[CustomerField] = "Selecciona el cliente que entrega el equipo.";
                }

                if (device is null)
                {
                    errors[DeviceField] = "Selecciona el equipo que se recibe.";
                }
                else if (input.CustomerId is > 0 && device.CustomerId != input.CustomerId)
                {
                    errors[DeviceField] = "El equipo seleccionado no pertenece a este cliente.";
                }
                else if (device.Customer is { IsActive: false })
                {
                    errors[CustomerField] = "El cliente está inactivo. Reactívalo para registrarle órdenes nuevas.";
                }

                Technician? technician = null;
                if (input.TechnicianId is { } technicianId)
                {
                    technician = await FindAssignableTechnicianAsync(technicianId, errors, token);
                }

                if (errors.Count > 0)
                {
                    return CommandResult<int>.Invalid(errors);
                }

                var order = new ServiceOrder(
                    device!, issue, input.Priority!.Value, receivedAt, input.PromisedAtUtc, input.EstimatedCost, notes, technician, currentUser.Actor);
                orders.Add(order);
                await unitOfWork.SaveChangesAsync(token);
                return CommandResult<int>.Success(order.Id);
            },
            cancellationToken);
    }

    public Task<CommandResult> AssignTechnicianAsync(int id, int? technicianId, string? note, int version, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var (order, problem) = await FindForChangeAsync(id, version, token);
                if (order is null)
                {
                    return problem!;
                }

                var errors = new Dictionary<string, string>();
                if (technicianId is not > 0)
                {
                    errors[TechnicianField] = "Selecciona el técnico responsable.";
                }
                else if (order.TechnicianId == technicianId)
                {
                    errors[TechnicianField] = "La orden ya está asignada a este técnico.";
                }

                if (Text.CleanOrNull(note) is { Length: > ServiceOrderAssignment.NoteMaxLength })
                {
                    errors[AssignmentNoteField] = $"El motivo no puede superar {ServiceOrderAssignment.NoteMaxLength} caracteres.";
                }

                var technician = errors.Count == 0 ? await FindAssignableTechnicianAsync(technicianId!.Value, errors, token) : null;
                if (errors.Count > 0)
                {
                    return CommandResult.Invalid(errors);
                }

                // La asignación y su registro (técnico anterior y nuevo, quién y por qué) se guardan juntos.
                order.AssignTechnician(technician!, UtcNow, currentUser.Actor, note);
                return await SaveAsync(token);
            },
            cancellationToken);

    public async Task<CommandResult> RecordDiagnosisAsync(int id, string? diagnosis, int version, CancellationToken cancellationToken)
    {
        var (order, problem) = await FindForChangeAsync(id, version, cancellationToken);
        if (order is null)
        {
            return problem!;
        }

        var cleaned = Text.CleanMultilineOrNull(diagnosis);
        var error = !ServiceOrderWorkflow.DiagnosisStatuses.Contains(order.Status)
            ? "El diagnóstico se registra mientras la orden está en diagnóstico, en reparación o en espera de repuestos."
            : ServiceOrderRules.ValidateDiagnosis(cleaned);
        if (error is not null)
        {
            return CommandResult.Invalid(new Dictionary<string, string> { [nameof(ServiceOrder.Diagnosis)] = error });
        }

        order.RecordDiagnosis(cleaned, UtcNow);
        return await SaveAsync(cancellationToken);
    }

    public async Task<CommandResult> UpdatePlanAsync(int id, ServiceOrderPlanInput input, int version, CancellationToken cancellationToken)
    {
        var (order, problem) = await FindForChangeAsync(id, version, cancellationToken);
        if (order is null)
        {
            return problem!;
        }

        var now = UtcNow;
        var errors = order.ValidatePlanChange(input.Priority, input.PromisedAtUtc, input.EstimatedCost, now);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        order.UpdatePlan(input.Priority!.Value, input.PromisedAtUtc, input.EstimatedCost, now);
        return await SaveAsync(cancellationToken);
    }

    public async Task<CommandResult> ChangeStatusAsync(int id, ServiceOrderStatusInput input, int version, CancellationToken cancellationToken)
    {
        var (order, problem) = await FindForChangeAsync(id, version, cancellationToken);
        if (order is null)
        {
            return problem!;
        }

        if (input.Status is not { } newStatus)
        {
            return CommandResult.Invalid(new Dictionary<string, string> { [nameof(ServiceOrder.Status)] = "Selecciona el nuevo estado." });
        }

        var errors = order.ValidateStatusChange(newStatus, input.Note, input.FinalCost);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        try
        {
            order.ChangeStatus(newStatus, UtcNow, input.Note, input.FinalCost, currentUser.Actor);
        }
        catch (DomainException exception)
        {
            // Solo queda la verificación de la fecha: el reloj del servidor retrocedió respecto del último cambio.
            return CommandResult.Conflict(exception.Message);
        }

        // El cambio de estado y su registro en el historial se guardan en la misma operación (un solo SaveChanges).
        return await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Carga la orden para modificarla y verifica que el usuario esté viendo su versión actual y que siga abierta.
    /// Si algo falla, devuelve el resultado que debe informarse.
    /// </summary>
    private async Task<(ServiceOrder? Order, CommandResult? Problem)> FindForChangeAsync(int id, int version, CancellationToken cancellationToken)
    {
        var order = id > 0 ? await orders.FindAsync(id, cancellationToken) : null;
        if (order is null)
        {
            return (null, CommandResult.NotFound());
        }

        if (order.Version != version)
        {
            return (null, CommandResult.Conflict(StaleMessage));
        }

        return order.IsClosed ? (null, CommandResult.Conflict(ClosedMessage)) : (order, null);
    }

    private async Task<Technician?> FindAssignableTechnicianAsync(int technicianId, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        var technician = technicianId > 0 ? await technicians.FindForAssignmentAsync(technicianId, cancellationToken) : null;
        if (technician is null)
        {
            errors[TechnicianField] = "Selecciona un técnico de la lista.";
        }
        else if (!technician.IsActive)
        {
            errors[TechnicianField] = "El técnico seleccionado está inactivo y no puede recibir asignaciones.";
        }

        return technician;
    }

    private async Task<CommandResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CommandResult.Success();
        }
        catch (ConcurrencyConflictException)
        {
            return CommandResult.Conflict(StaleMessage);
        }
    }

    private static bool BelongsTo(ServiceOrderView view, ServiceOrderStatus status) => view switch
    {
        ServiceOrderView.Open or ServiceOrderView.Overdue => ServiceOrderWorkflow.IsOpen(status),
        ServiceOrderView.Closed => ServiceOrderWorkflow.IsClosed(status),
        _ => true,
    };
}
