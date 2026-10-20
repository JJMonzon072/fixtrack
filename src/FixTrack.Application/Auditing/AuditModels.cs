using FixTrack.Application.Common;

namespace FixTrack.Application.Auditing;

/// <summary>Áreas en las que se agrupan los eventos de auditoría.</summary>
public enum AuditArea
{
    All,
    Orders,
    Technicians,
    Customers,
    Accounts,
}

public sealed record AuditSearch(AuditArea Area, int Page)
{
    public const int PageSize = 50;
}

/// <summary>Un evento tal como se muestra: el autor y el resumen se copiaron en el momento de la operación.</summary>
public sealed record AuditEntry(DateTime OccurredAtUtc, string UserName, string Action, string EntityType, string EntityId, string Summary);

/// <param name="Counts">Cantidad de eventos por área (para las pestañas).</param>
public sealed record AuditSearchResult(PagedResult<AuditEntry> Page, IReadOnlyDictionary<AuditArea, int> Counts);

/// <summary>Consulta del registro de auditoría (solo lectura).</summary>
public interface IAuditTrail
{
    /// <summary>Eventos del área, del más reciente al más antiguo, paginados en SQL.</summary>
    Task<AuditSearchResult> SearchAsync(AuditSearch search, CancellationToken cancellationToken);
}
