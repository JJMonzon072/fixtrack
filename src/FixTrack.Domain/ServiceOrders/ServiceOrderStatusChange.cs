using FixTrack.Domain.Common;

namespace FixTrack.Domain.ServiceOrders;

/// <summary>
/// Registro de un cambio de estado de una orden (tabla ServiceOrderStatusHistory).
/// Solo se agrega desde <see cref="ServiceOrder"/> y nunca se modifica ni se elimina.
/// </summary>
public sealed class ServiceOrderStatusChange : Entity
{
    public const int NoteMaxLength = 500;

    internal ServiceOrderStatusChange(ServiceOrderStatus? fromStatus, ServiceOrderStatus toStatus, DateTime changedAtUtc, string? note, Actor? changedBy)
    {
        var cleanNote = Text.CleanOrNull(note);
        if (cleanNote is { Length: > NoteMaxLength })
        {
            throw new DomainException($"La nota no puede superar {NoteMaxLength} caracteres.");
        }

        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAtUtc = UtcClock.Ensure(changedAtUtc, nameof(changedAtUtc));
        Note = cleanNote;
        ChangedByUserId = changedBy?.UserId;
        ChangedByName = changedBy?.Name;
    }

    // Constructor para Entity Framework Core.
    private ServiceOrderStatusChange()
    {
    }

    public int ServiceOrderId { get; private set; }

    /// <summary>Estado anterior. Es <c>null</c> en el registro que crea la orden.</summary>
    public ServiceOrderStatus? FromStatus { get; private set; }

    public ServiceOrderStatus ToStatus { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Cuenta que hizo el cambio. Es <c>null</c> en los registros anteriores a la fase 4.</summary>
    public string? ChangedByUserId { get; private set; }

    /// <summary>Nombre de quien hizo el cambio, copiado en ese momento.</summary>
    public string? ChangedByName { get; private set; }
}
