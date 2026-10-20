using FixTrack.Domain.Common;
using FixTrack.Domain.Devices;
using FixTrack.Domain.Technicians;

namespace FixTrack.Domain.ServiceOrders;

/// <summary>
/// Orden de servicio sobre un equipo. Toda orden nace en estado <see cref="ServiceOrderStatus.Received"/>
/// y cada cambio de estado queda registrado en <see cref="StatusHistory"/>.
/// </summary>
public sealed class ServiceOrder : Entity
{
    public const int ReportedIssueMaxLength = 1000;
    public const int DiagnosisMaxLength = 2000;
    public const decimal MaxAmount = 99_999_999.99m;

    private readonly List<ServiceOrderStatusChange> _statusHistory = [];

    public ServiceOrder(
        int deviceId,
        string reportedIssue,
        ServiceOrderPriority priority,
        DateTime receivedAtUtc,
        DateTime? promisedAtUtc,
        decimal? estimatedCost)
    {
        if (deviceId <= 0)
        {
            throw new DomainException("La orden debe estar asociada a un equipo.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException("La prioridad no es válida.");
        }

        var issue = Text.CleanMultilineOrNull(reportedIssue)
            ?? throw new DomainException("Describe el problema reportado por el cliente.");
        if (issue.Length > ReportedIssueMaxLength)
        {
            throw new DomainException($"El problema reportado no puede superar {ReportedIssueMaxLength} caracteres.");
        }

        ReceivedAtUtc = UtcClock.Ensure(receivedAtUtc, nameof(receivedAtUtc));
        if (promisedAtUtc is { } promised && UtcClock.Ensure(promised, nameof(promisedAtUtc)) < ReceivedAtUtc)
        {
            throw new DomainException("La fecha de entrega comprometida no puede ser anterior a la recepción.");
        }

        DeviceId = deviceId;
        ReportedIssue = issue;
        Priority = priority;
        PromisedAtUtc = promisedAtUtc;
        EstimatedCost = ValidateAmount(estimatedCost, "El costo estimado");
        Status = ServiceOrderStatus.Received;
        CreatedAtUtc = ReceivedAtUtc;
        _statusHistory.Add(new ServiceOrderStatusChange(null, ServiceOrderStatus.Received, ReceivedAtUtc, "Orden registrada."));
    }

    // Constructor para Entity Framework Core.
    private ServiceOrder()
    {
    }

    /// <summary>Número visible de la orden, por ejemplo OS-000042.</summary>
    public string Number => $"OS-{Id:D6}";

    public int DeviceId { get; private set; }

    public Device? Device { get; private set; }

    /// <summary>Técnico responsable. Una orden puede registrarse sin técnico asignado.</summary>
    public int? TechnicianId { get; private set; }

    public Technician? Technician { get; private set; }

    public ServiceOrderStatus Status { get; private set; }

    public ServiceOrderPriority Priority { get; private set; }

    public string ReportedIssue { get; private set; } = string.Empty;

    public string? Diagnosis { get; private set; }

    public decimal? EstimatedCost { get; private set; }

    public decimal? FinalCost { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>Fecha de entrega comprometida con el cliente.</summary>
    public DateTime? PromisedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ServiceOrderStatusChange> StatusHistory => _statusHistory;

    /// <summary>Una orden entregada o cancelada ya no admite cambios de estado.</summary>
    public bool IsClosed => Status is ServiceOrderStatus.Delivered or ServiceOrderStatus.Cancelled;

    public void AssignTechnician(int technicianId, DateTime assignedAtUtc)
    {
        EnsureOpen();
        if (technicianId <= 0)
        {
            throw new DomainException("El técnico no es válido.");
        }

        TechnicianId = technicianId;
        UpdatedAtUtc = UtcClock.Ensure(assignedAtUtc, nameof(assignedAtUtc));
    }

    public void ChangeStatus(ServiceOrderStatus newStatus, DateTime changedAtUtc, string? note = null, decimal? finalCost = null)
    {
        EnsureOpen();
        if (!Enum.IsDefined(newStatus))
        {
            throw new DomainException("El estado no es válido.");
        }

        if (newStatus == Status)
        {
            throw new DomainException("La orden ya se encuentra en ese estado.");
        }

        if (newStatus == ServiceOrderStatus.Received)
        {
            throw new DomainException("Una orden no puede volver al estado Recibida.");
        }

        if (newStatus == ServiceOrderStatus.Delivered && Status != ServiceOrderStatus.ReadyForPickup)
        {
            throw new DomainException("Solo se puede entregar una orden que está lista para entrega.");
        }

        var changedAt = UtcClock.Ensure(changedAtUtc, nameof(changedAtUtc));
        if (changedAt < ReceivedAtUtc)
        {
            throw new DomainException("El cambio de estado no puede ser anterior a la recepción.");
        }

        _statusHistory.Add(new ServiceOrderStatusChange(Status, newStatus, changedAt, note));
        Status = newStatus;
        UpdatedAtUtc = changedAt;

        if (newStatus == ServiceOrderStatus.Delivered)
        {
            DeliveredAtUtc = changedAt;
            FinalCost = ValidateAmount(finalCost, "El costo final") ?? EstimatedCost;
        }
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainException("La orden está cerrada y no admite cambios.");
        }
    }

    private static decimal? ValidateAmount(decimal? amount, string field) =>
        amount switch
        {
            null => null,
            < 0 => throw new DomainException($"{field} no puede ser negativo."),
            > MaxAmount => throw new DomainException($"{field} supera el máximo permitido."),
            _ when decimal.Round(amount.Value, 2) != amount.Value =>
                throw new DomainException($"{field} admite como máximo dos decimales."),
            _ => amount,
        };
}
