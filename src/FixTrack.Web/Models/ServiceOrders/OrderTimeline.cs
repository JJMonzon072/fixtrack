using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Auditing;

namespace FixTrack.Web.Models.ServiceOrders;

/// <summary>Un evento de la línea de tiempo de una orden: cambio de estado o asignación de técnico.</summary>
/// <param name="Tone">Valor de <c>data-status</c>: define el color del ícono.</param>
/// <param name="Detail">Texto secundario junto al título (por ejemplo, "desde Recibida").</param>
/// <param name="Author">Quién hizo el cambio; <c>null</c> en los registros anteriores a que se guardara.</param>
public sealed record TimelineEntry(string Tone, string Icon, string Title, string? Detail, DateTime AtUtc, string? Author, string? Note);

/// <summary>
/// Une en orden cronológico el historial de estados, el de asignaciones y los cambios de planificación y diagnóstico de
/// la auditoría. Son registros distintos (un cambio de técnico no cambia el estado), pero se leen juntos. Con la misma
/// hora, el estado va antes que la asignación y esta antes que los demás cambios.
/// </summary>
public static class OrderTimeline
{
    public static IReadOnlyList<TimelineEntry> Build(ServiceOrderDetails order)
    {
        var statuses = order.History.Select((entry, index) => (Kind: 0, Index: index, Entry: new TimelineEntry(
            DomainDisplay.Tone(entry.ToStatus),
            DomainDisplay.Icon(entry.ToStatus),
            DomainDisplay.Name(entry.ToStatus),
            entry.FromStatus is { } from ? $"desde {DomainDisplay.Name(from)}" : null,
            entry.ChangedAtUtc,
            entry.ChangedByName,
            entry.Note)));

        var assignments = order.Assignments.Select((entry, index) => (Kind: 1, Index: index, Entry: new TimelineEntry(
            "assignment",
            entry.IsReassignment ? "arrow-right-left" : "user-check",
            entry.IsReassignment ? $"Reasignada a {entry.ToTechnicianName}" : $"Técnico asignado: {entry.ToTechnicianName}",
            entry.IsReassignment ? $"antes: {entry.FromTechnicianName}" : null,
            entry.AssignedAtUtc,
            entry.AssignedByName,
            entry.Note)));

        var activity = order.Activity.Select((entry, index) => (Kind: 2, Index: index, Entry: new TimelineEntry(
            "note",
            entry.Action == AuditActions.PlanChanged ? "calendar-clock" : "scan-search",
            entry.Action == AuditActions.PlanChanged ? "Planificación actualizada" : "Diagnóstico",
            null,
            entry.OccurredAtUtc,
            entry.UserName,
            entry.Summary)));

        return [.. statuses.Concat(assignments).Concat(activity)
            .OrderBy(item => item.Entry.AtUtc)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry)];
    }
}
