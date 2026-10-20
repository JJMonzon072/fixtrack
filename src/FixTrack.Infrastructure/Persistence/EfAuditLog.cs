using FixTrack.Application.Common;
using FixTrack.Domain.Auditing;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>Agrega el evento al contexto compartido por la solicitud: se guarda con el resto de la operación.</summary>
internal sealed class EfAuditLog(FixTrackDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    public void Record(string action, string entityType, string entityId, string summary) =>
        db.AuditEvents.Add(new AuditEvent(action, entityType, entityId, summary, currentUser.Actor, clock.GetUtcNow().UtcDateTime));
}
