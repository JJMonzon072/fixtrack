using System.Globalization;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Common;

/// <summary>Formatos de los resúmenes de auditoría: estables e independientes de la cultura del servidor.</summary>
public static class AuditText
{
    public static string Money(decimal? amount) =>
        amount is { } value ? "Q " + value.ToString("N2", CultureInfo.InvariantCulture) : "sin monto";

    /// <summary>Fecha en UTC: el registro no depende de la zona horaria del servidor.</summary>
    public static string Date(DateTime? utc) =>
        utc is { } value ? value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "sin fecha";

    public static string Priority(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => "Baja",
        ServiceOrderPriority.Normal => "Normal",
        ServiceOrderPriority.High => "Alta",
        _ => "Urgente",
    };

    public static string Status(ServiceOrderStatus status) => ServiceOrderWorkflow.Name(status);
}
