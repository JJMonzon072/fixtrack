using FixTrack.Domain.Common;
using FixTrack.Domain.Technicians;

namespace FixTrack.Domain.ServiceOrders;

/// <summary>
/// Asignación o reasignación del técnico de una orden (tabla ServiceOrderAssignments): técnico anterior y nuevo, cuándo,
/// quién la hizo y por qué. Solo se agrega desde <see cref="ServiceOrder"/>, en el mismo guardado que la asignación, y
/// nunca se modifica ni se elimina. Es un historial distinto del de estados: una reasignación no cambia el estado.
/// </summary>
public sealed class ServiceOrderAssignment : Entity
{
    public const int NoteMaxLength = 200;

    internal ServiceOrderAssignment(int? fromTechnicianId, Technician to, DateTime assignedAtUtc, Actor? assignedBy, string? note)
    {
        var cleanNote = Text.CleanOrNull(note);
        if (cleanNote is { Length: > NoteMaxLength })
        {
            throw new DomainException($"El motivo no puede superar {NoteMaxLength} caracteres.");
        }

        FromTechnicianId = fromTechnicianId;
        ToTechnician = to;
        ToTechnicianId = to.Id;
        AssignedAtUtc = UtcClock.Ensure(assignedAtUtc, nameof(assignedAtUtc));
        AssignedByUserId = assignedBy?.UserId;
        AssignedByName = assignedBy?.Name;
        Note = cleanNote;
    }

    // Constructor para Entity Framework Core.
    private ServiceOrderAssignment()
    {
    }

    public int ServiceOrderId { get; private set; }

    /// <summary>Técnico anterior; <c>null</c> en la primera asignación.</summary>
    public int? FromTechnicianId { get; private set; }

    public int ToTechnicianId { get; private set; }

    public Technician? ToTechnician { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    public string? AssignedByUserId { get; private set; }

    public string? AssignedByName { get; private set; }

    /// <summary>Motivo u observación de la reasignación (opcional).</summary>
    public string? Note { get; private set; }
}
