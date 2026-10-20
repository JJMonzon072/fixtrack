using FixTrack.Domain.Common;
using FixTrack.Domain.Devices;
using FixTrack.Domain.Technicians;

namespace FixTrack.Domain.ServiceOrders;

/// <summary>
/// Orden de servicio sobre un equipo. Toda orden nace en estado <see cref="ServiceOrderStatus.Received"/>
/// y cada cambio de estado queda registrado en <see cref="StatusHistory"/>. Los cambios se hacen
/// exclusivamente con los métodos de esta clase, que aplican el flujo de <see cref="ServiceOrderWorkflow"/>.
/// </summary>
public sealed class ServiceOrder : Entity
{
    private readonly List<ServiceOrderStatusChange> _statusHistory = [];

    /// <summary>Registra la recepción de un equipo. El propietario del equipo debe estar cargado y activo.</summary>
    /// <param name="technician">Técnico responsable desde la recepción; puede asignarse después.</param>
    public ServiceOrder(
        Device device,
        string? reportedIssue,
        ServiceOrderPriority priority,
        DateTime receivedAtUtc,
        DateTime? promisedAtUtc,
        decimal? estimatedCost,
        string? intakeNotes,
        Technician? technician = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var owner = device.Customer
            ?? throw new InvalidOperationException("El propietario del equipo debe estar cargado para registrar la orden.");
        if (!owner.IsActive)
        {
            throw new DomainException("No se pueden registrar órdenes para un cliente inactivo.");
        }

        var issue = Text.CleanMultilineOrNull(reportedIssue);
        var notes = Text.CleanMultilineOrNull(intakeNotes);
        ReceivedAtUtc = UtcClock.Ensure(receivedAtUtc, nameof(receivedAtUtc));
        if (promisedAtUtc is { } promised)
        {
            UtcClock.Ensure(promised, nameof(promisedAtUtc));
        }

        ThrowIfInvalid(ServiceOrderRules.ValidateIntake(issue, notes));
        ThrowIfInvalid(ServiceOrderRules.ValidatePlan(priority, ReceivedAtUtc, promisedAtUtc, estimatedCost));

        Device = device;
        DeviceId = device.Id;
        ReportedIssue = issue!;
        IntakeNotes = notes;
        Priority = priority;
        PromisedAtUtc = promisedAtUtc;
        EstimatedCost = estimatedCost;
        Status = ServiceOrderStatus.Received;
        CreatedAtUtc = ReceivedAtUtc;
        _statusHistory.Add(new ServiceOrderStatusChange(null, ServiceOrderStatus.Received, ReceivedAtUtc, "Orden registrada."));

        if (technician is not null)
        {
            SetTechnician(technician, ReceivedAtUtc);
        }
    }

    // Constructor para Entity Framework Core.
    private ServiceOrder()
    {
    }

    /// <summary>Número visible de la orden, por ejemplo OS-000042. Se deriva del Id y no puede editarse.</summary>
    public string Number => FormatNumber(Id);

    public int DeviceId { get; private set; }

    public Device? Device { get; private set; }

    /// <summary>Técnico responsable. Una orden puede registrarse sin técnico asignado.</summary>
    public int? TechnicianId { get; private set; }

    public Technician? Technician { get; private set; }

    /// <summary>Momento de la asignación vigente del técnico.</summary>
    public DateTime? TechnicianAssignedAtUtc { get; private set; }

    public ServiceOrderStatus Status { get; private set; }

    public ServiceOrderPriority Priority { get; private set; }

    public string ReportedIssue { get; private set; } = string.Empty;

    /// <summary>Estado físico y accesorios con los que se recibió el equipo. No cambia después de la recepción.</summary>
    public string? IntakeNotes { get; private set; }

    public string? Diagnosis { get; private set; }

    public decimal? EstimatedCost { get; private set; }

    /// <summary>Monto cobrado al entregar. Solo existe en las órdenes entregadas.</summary>
    public decimal? FinalCost { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>Fecha de entrega comprometida con el cliente.</summary>
    public DateTime? PromisedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Versión de la orden. Aumenta con cada cambio y sirve como control de concurrencia optimista:
    /// si otra persona modificó la orden, el guardado se rechaza en lugar de sobrescribir su cambio.
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ServiceOrderStatusChange> StatusHistory => _statusHistory;

    /// <summary>Una orden entregada o cancelada ya no admite cambios.</summary>
    public bool IsClosed => ServiceOrderWorkflow.IsClosed(Status);

    /// <summary>Diferencia entre el costo final y el estimado; <c>null</c> si falta alguno de los dos.</summary>
    public decimal? CostVariance => FinalCost - EstimatedCost;

    public static string FormatNumber(int id) => $"OS-{id:D6}";

    /// <summary>Asigna o reasigna el técnico responsable. Solo los técnicos activos pueden recibir asignaciones.</summary>
    public void AssignTechnician(Technician technician, DateTime assignedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(technician);
        EnsureOpen();
        if (TechnicianId is { } current && !technician.IsTransient && current == technician.Id)
        {
            throw new DomainException("La orden ya está asignada a este técnico.");
        }

        SetTechnician(technician, assignedAtUtc);
        Touch(assignedAtUtc);
    }

    /// <summary>Registra o actualiza el diagnóstico técnico mientras la orden está en el banco de trabajo.</summary>
    public void RecordDiagnosis(string? diagnosis, DateTime recordedAtUtc)
    {
        EnsureOpen();
        if (!ServiceOrderWorkflow.DiagnosisStatuses.Contains(Status))
        {
            throw new DomainException(Status == ServiceOrderStatus.Received
                ? "Inicia el diagnóstico antes de registrarlo."
                : "El diagnóstico solo puede modificarse mientras la orden está en diagnóstico, en reparación o en espera de repuestos.");
        }

        var cleaned = Text.CleanMultilineOrNull(diagnosis);
        if (ServiceOrderRules.ValidateDiagnosis(cleaned) is { } error)
        {
            throw new DomainException(error);
        }

        Diagnosis = cleaned;
        Touch(recordedAtUtc);
    }

    /// <summary>
    /// Actualiza la prioridad, la entrega comprometida y el costo estimado de una orden abierta.
    /// Una nueva fecha comprometida no puede quedar en el pasado.
    /// </summary>
    public void UpdatePlan(ServiceOrderPriority priority, DateTime? promisedAtUtc, decimal? estimatedCost, DateTime updatedAtUtc)
    {
        EnsureOpen();
        var updatedAt = UtcClock.Ensure(updatedAtUtc, nameof(updatedAtUtc));
        if (promisedAtUtc is { } promised)
        {
            UtcClock.Ensure(promised, nameof(promisedAtUtc));
        }

        ThrowIfInvalid(ValidatePlanChange(priority, promisedAtUtc, estimatedCost, updatedAt));

        Priority = priority;
        PromisedAtUtc = promisedAtUtc;
        EstimatedCost = estimatedCost;
        Touch(updatedAt);
    }

    /// <summary>Revisa una nueva planificación sin aplicarla. Las claves son los nombres de las propiedades.</summary>
    public IReadOnlyDictionary<string, string> ValidatePlanChange(
        ServiceOrderPriority? priority,
        DateTime? promisedAtUtc,
        decimal? estimatedCost,
        DateTime nowUtc)
    {
        var errors = new Dictionary<string, string>(ServiceOrderRules.ValidatePlan(priority, ReceivedAtUtc, promisedAtUtc, estimatedCost));
        if (promisedAtUtc is { } promised
            && promised != PromisedAtUtc
            && promised < nowUtc
            && !errors.ContainsKey(nameof(PromisedAtUtc)))
        {
            errors[nameof(PromisedAtUtc)] = "La nueva fecha de entrega comprometida no puede estar en el pasado.";
        }

        return errors;
    }

    /// <summary>
    /// Cambia el estado siguiendo <see cref="ServiceOrderWorkflow"/> y agrega el registro al historial.
    /// Entregar exige el costo final; cancelar y esperar repuestos exigen una nota.
    /// </summary>
    public void ChangeStatus(ServiceOrderStatus newStatus, DateTime changedAtUtc, string? note = null, decimal? finalCost = null)
    {
        ThrowIfInvalid(ValidateStatusChange(newStatus, note, finalCost));

        var changedAt = UtcClock.Ensure(changedAtUtc, nameof(changedAtUtc));
        if (changedAt < (UpdatedAtUtc ?? ReceivedAtUtc))
        {
            throw new DomainException("El cambio de estado no puede ser anterior al último cambio registrado en la orden.");
        }

        _statusHistory.Add(new ServiceOrderStatusChange(Status, newStatus, changedAt, Text.CleanOrNull(note)));
        Status = newStatus;
        if (newStatus == ServiceOrderStatus.Delivered)
        {
            DeliveredAtUtc = changedAt;
            FinalCost = finalCost;
        }

        Touch(changedAt);
    }

    /// <summary>
    /// Revisa si la orden puede pasar a <paramref name="newStatus"/> con esa nota y ese costo final, sin modificarla.
    /// Las claves del resultado son <c>Status</c>, <c>Note</c> y <c>FinalCost</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ValidateStatusChange(ServiceOrderStatus newStatus, string? note, decimal? finalCost)
    {
        var errors = new Dictionary<string, string>();
        if (StatusError(newStatus) is { } statusError)
        {
            errors[nameof(Status)] = statusError;
        }

        var cleanNote = Text.CleanOrNull(note);
        if (cleanNote is { Length: > ServiceOrderStatusChange.NoteMaxLength })
        {
            errors[nameof(ServiceOrderStatusChange.Note)] = $"La nota no puede superar {ServiceOrderStatusChange.NoteMaxLength} caracteres.";
        }
        else if (cleanNote is null && newStatus == ServiceOrderStatus.AwaitingParts)
        {
            errors[nameof(ServiceOrderStatusChange.Note)] = "Indica qué repuesto se está esperando.";
        }
        else if (cleanNote is null && newStatus == ServiceOrderStatus.Cancelled)
        {
            errors[nameof(ServiceOrderStatusChange.Note)] = "Indica el motivo de la cancelación.";
        }

        if (newStatus == ServiceOrderStatus.Delivered)
        {
            var costError = finalCost is null
                ? "Indica el costo final para entregar la orden."
                : ServiceOrderRules.ValidateAmount(finalCost, "El costo final");
            if (costError is not null)
            {
                errors[nameof(FinalCost)] = costError;
            }
        }
        else if (finalCost is not null)
        {
            errors[nameof(FinalCost)] = "El costo final solo se registra al entregar la orden.";
        }

        return errors;
    }

    private string? StatusError(ServiceOrderStatus newStatus) =>
        ServiceOrderWorkflow.TransitionBlocker(Status, newStatus, TechnicianId is not null, Diagnosis is not null);

    private void SetTechnician(Technician technician, DateTime assignedAtUtc)
    {
        if (!technician.IsActive)
        {
            throw new DomainException("No se puede asignar un técnico inactivo.");
        }

        Technician = technician;
        TechnicianId = technician.Id;
        TechnicianAssignedAtUtc = UtcClock.Ensure(assignedAtUtc, nameof(assignedAtUtc));
    }

    private void Touch(DateTime changedAtUtc)
    {
        UpdatedAtUtc = UtcClock.Ensure(changedAtUtc, nameof(changedAtUtc));
        Version++;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainException("La orden está cerrada (entregada o cancelada) y no admite cambios.");
        }
    }

    private static void ThrowIfInvalid(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.Count > 0)
        {
            throw new DomainException(errors.Values.First());
        }
    }
}
