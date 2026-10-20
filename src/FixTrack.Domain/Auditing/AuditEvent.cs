using FixTrack.Domain.Common;

namespace FixTrack.Domain.Auditing;

/// <summary>
/// Registro de una operación importante (tabla AuditEvents): quién, cuándo, qué tipo de cambio y sobre qué registro,
/// con un resumen legible. Es de solo inserción. Nunca guarda contraseñas, tokens ni cadenas de conexión: el resumen lo
/// arma el caso de uso con datos de negocio (nombres, estados, montos).
/// </summary>
public sealed class AuditEvent : Entity
{
    public const int ActionMaxLength = 40;
    public const int EntityTypeMaxLength = 20;
    public const int EntityIdMaxLength = 64;
    public const int SummaryMaxLength = 400;

    public AuditEvent(string action, string entityType, string entityId, string summary, Actor actor, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Action = Required(action, ActionMaxLength, nameof(action));
        EntityType = Required(entityType, EntityTypeMaxLength, nameof(entityType));
        EntityId = Required(entityId, EntityIdMaxLength, nameof(entityId));

        // El resumen se recorta en lugar de rechazar la operación: es descriptivo, no un dato del negocio.
        var text = Text.Clean(summary);
        Summary = text.Length > SummaryMaxLength ? string.Concat(text.AsSpan(0, SummaryMaxLength - 1), "…") : text;
        if (Summary.Length == 0)
        {
            throw new DomainException("El evento de auditoría necesita un resumen.");
        }

        UserId = actor.UserId;
        UserName = actor.Name;
        OccurredAtUtc = UtcClock.Ensure(occurredAtUtc, nameof(occurredAtUtc));
    }

    // Constructor para Entity Framework Core.
    private AuditEvent()
    {
    }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Cuenta que hizo la operación; <c>null</c> si fue el sistema (un comando de consola).</summary>
    public string? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    /// <summary>Tipo de cambio, por ejemplo "orden.estado" (ver <see cref="AuditActions"/>).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Tipo de registro afectado: "orden", "tecnico", "cliente" o "cuenta".</summary>
    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    private static string Required(string value, int maxLength, string name)
    {
        var clean = Text.Clean(value);
        return clean.Length is > 0 and var length && length <= maxLength
            ? clean
            : throw new ArgumentException($"El valor de {name} debe tener entre 1 y {maxLength} caracteres.", name);
    }
}
