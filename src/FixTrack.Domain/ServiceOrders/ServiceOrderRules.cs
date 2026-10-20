using System.Globalization;

namespace FixTrack.Domain.ServiceOrders;

/// <summary>Reglas de validación de los datos de una orden de servicio.</summary>
public static class ServiceOrderRules
{
    public const int ReportedIssueMaxLength = 1000;
    public const int DiagnosisMaxLength = 2000;
    public const int IntakeNotesMaxLength = 500;

    /// <summary>Monto máximo que admite una columna decimal(10,2).</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    /// <summary>Plazo máximo entre la recepción y la entrega comprometida.</summary>
    public static readonly TimeSpan MaxPromiseWindow = TimeSpan.FromDays(365);

    /// <summary>
    /// Valida los datos de planificación ya normalizados (prioridad, entrega comprometida y costo estimado).
    /// Las claves del resultado son los nombres de las propiedades de <see cref="ServiceOrder"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidatePlan(
        ServiceOrderPriority? priority,
        DateTime receivedAtUtc,
        DateTime? promisedAtUtc,
        decimal? estimatedCost)
    {
        var errors = new Dictionary<string, string>();

        if (priority is null || !Enum.IsDefined(priority.Value))
        {
            errors[nameof(ServiceOrder.Priority)] = "Selecciona la prioridad.";
        }

        if (promisedAtUtc is { } promised)
        {
            if (promised < receivedAtUtc)
            {
                errors[nameof(ServiceOrder.PromisedAtUtc)] = "La fecha de entrega comprometida no puede ser anterior a la recepción.";
            }
            else if (promised - receivedAtUtc > MaxPromiseWindow)
            {
                errors[nameof(ServiceOrder.PromisedAtUtc)] =
                    $"La fecha de entrega comprometida no puede estar a más de {MaxPromiseWindow.TotalDays:0} días de la recepción.";
            }
        }

        if (ValidateAmount(estimatedCost, "El costo estimado") is { } costError)
        {
            errors[nameof(ServiceOrder.EstimatedCost)] = costError;
        }

        return errors;
    }

    /// <summary>Valida el problema reportado y las observaciones de recepción, ya normalizados.</summary>
    public static IReadOnlyDictionary<string, string> ValidateIntake(string? reportedIssue, string? intakeNotes)
    {
        var errors = new Dictionary<string, string>();

        if (reportedIssue is null)
        {
            errors[nameof(ServiceOrder.ReportedIssue)] = "Describe el problema reportado por el cliente.";
        }
        else if (reportedIssue.Length > ReportedIssueMaxLength)
        {
            errors[nameof(ServiceOrder.ReportedIssue)] = $"El problema reportado no puede superar {ReportedIssueMaxLength} caracteres.";
        }

        if (intakeNotes is { Length: > IntakeNotesMaxLength })
        {
            errors[nameof(ServiceOrder.IntakeNotes)] = $"Las observaciones no pueden superar {IntakeNotesMaxLength} caracteres.";
        }

        return errors;
    }

    /// <summary>Valida un diagnóstico ya normalizado. Devuelve el mensaje de error o <c>null</c>.</summary>
    public static string? ValidateDiagnosis(string? diagnosis) =>
        diagnosis switch
        {
            null => "Describe el diagnóstico técnico.",
            { Length: > DiagnosisMaxLength } => $"El diagnóstico no puede superar {DiagnosisMaxLength} caracteres.",
            _ => null,
        };

    /// <summary>
    /// Valida un monto en quetzales: no negativo, con dos decimales como máximo y dentro del rango de decimal(10,2).
    /// </summary>
    /// <param name="field">Inicio del mensaje, por ejemplo "El costo estimado".</param>
    public static string? ValidateAmount(decimal? amount, string field) =>
        amount switch
        {
            null => null,
            < 0 => $"{field} no puede ser negativo.",
            > MaxAmount => $"{field} supera el máximo permitido (Q {MaxAmount.ToString("N2", CultureInfo.InvariantCulture)}).",
            _ when decimal.Round(amount.Value, 2) != amount.Value => $"{field} admite como máximo dos decimales.",
            _ => null,
        };
}
