namespace FixTrack.Application.Common;

/// <summary>
/// Registro de auditoría de operaciones importantes. <see cref="Record"/> agrega el evento a la operación en curso: se
/// guarda con el siguiente guardado, dentro de la misma transacción, y no queda nada si la operación falla. El autor y la
/// fecha los pone la implementación (<see cref="ICurrentUser"/> y el reloj).
/// </summary>
public interface IAuditLog
{
    /// <param name="action">Tipo de cambio (constantes de <c>AuditActions</c>).</param>
    /// <param name="summary">Resumen legible, sin contraseñas, tokens ni otros secretos.</param>
    void Record(string action, string entityType, string entityId, string summary);
}
