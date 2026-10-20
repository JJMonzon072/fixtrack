namespace FixTrack.Domain.ServiceOrders;

/// <summary>Lista de trabajo en la que aparece una orden abierta.</summary>
public enum WorkQueue
{
    /// <summary>Recibida, en diagnóstico o en reparación: el taller puede trabajar en ella ahora.</summary>
    Repair,

    /// <summary>Detenida hasta recibir un repuesto: no se recomienda como reparación inmediata.</summary>
    AwaitingParts,

    /// <summary>Terminada: la acción pendiente es avisar al cliente y entregarla.</summary>
    ReadyForPickup,
}

/// <summary>Acción que el algoritmo recomienda para una orden.</summary>
public enum RecommendedAction
{
    AssignTechnician,
    StartDiagnosis,
    CompleteDiagnosis,
    ContinueRepair,
    FollowUpParts,
    DeliverToCustomer,
}

/// <summary>Un componente de la puntuación, con su explicación en español.</summary>
public sealed record ScoreFactor(string Description, int Points);

/// <summary>Resultado de priorizar una orden: puntuación total, cómo se obtuvo, lista y acción recomendada.</summary>
public sealed record WorkPriorityScore(int Total, IReadOnlyList<ScoreFactor> Factors, WorkQueue Queue, RecommendedAction Action);

/// <summary>
/// Algoritmo de priorización de órdenes abiertas. Es determinista y explicable: la puntuación es la suma de
/// tres factores independientes, y cada uno queda registrado con su explicación.
/// <list type="number">
/// <item>Prioridad configurada: baja 10, normal 30, alta 60, urgente 90.</item>
/// <item>
/// Fecha de entrega (solo uno de estos bonos, porque se excluyen entre sí): vencida +50; vence dentro de las
/// próximas 24 horas +30; vence entre 24 y 72 horas +15; más adelante o sin fecha comprometida, 0.
/// </item>
/// <item>Antigüedad: +2 por cada día completo desde la recepción, hasta un máximo de 30 (15 días).</item>
/// </list>
/// El estado no suma puntos: decide en qué lista aparece la orden (<see cref="WorkQueue"/>) y qué acción se
/// recomienda. Así, una orden en espera de repuestos nunca desplaza a una reparación que sí puede ejecutarse,
/// y una orden lista para entrega aparece entre las entregas pendientes.
/// </summary>
public static class WorkPrioritizer
{
    public const int LowPriorityPoints = 10;
    public const int NormalPriorityPoints = 30;
    public const int HighPriorityPoints = 60;
    public const int UrgentPriorityPoints = 90;
    public const int OverduePoints = 50;
    public const int DueWithin24HoursPoints = 30;
    public const int DueWithin72HoursPoints = 15;
    public const int PointsPerDayOfAge = 2;
    public const int MaxAgePoints = 30;

    public static readonly TimeSpan NearDueWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan SoonDueWindow = TimeSpan.FromHours(72);

    /// <summary>Calcula la puntuación de una orden abierta en el momento <paramref name="nowUtc"/>.</summary>
    /// <exception cref="ArgumentException">Si la orden está entregada o cancelada: las órdenes cerradas no se priorizan.</exception>
    public static WorkPriorityScore Score(
        ServiceOrderPriority priority,
        ServiceOrderStatus status,
        DateTime receivedAtUtc,
        DateTime? promisedAtUtc,
        bool hasTechnician,
        DateTime nowUtc)
    {
        if (!ServiceOrderWorkflow.IsOpen(status))
        {
            throw new ArgumentException("Solo se priorizan órdenes abiertas.", nameof(status));
        }

        var factors = new List<ScoreFactor>
        {
            new($"Prioridad {PriorityName(priority)}", PriorityPoints(priority)),
        };

        if (DueFactor(promisedAtUtc, nowUtc) is { } due)
        {
            factors.Add(due);
        }

        var days = Math.Max(0, (int)Math.Floor((nowUtc - receivedAtUtc).TotalDays));
        var agePoints = Math.Min(days * PointsPerDayOfAge, MaxAgePoints);
        if (agePoints > 0)
        {
            var description = days == 1 ? "1 día en el taller" : $"{days} días en el taller";
            factors.Add(new ScoreFactor(agePoints == MaxAgePoints ? $"{description} (máximo)" : description, agePoints));
        }

        return new WorkPriorityScore(factors.Sum(factor => factor.Points), factors, QueueFor(status), ActionFor(status, hasTechnician));
    }

    /// <summary>
    /// Ordena órdenes ya puntuadas de forma estable: mayor puntuación, entrega comprometida más cercana (sin fecha al
    /// final), recepción más antigua y, por último, el número de orden.
    /// </summary>
    public static IOrderedEnumerable<T> Rank<T>(
        IEnumerable<T> items,
        Func<T, int> score,
        Func<T, DateTime?> promisedAtUtc,
        Func<T, DateTime> receivedAtUtc,
        Func<T, int> id) =>
        items
            .OrderByDescending(score)
            .ThenBy(item => promisedAtUtc(item) is null)
            .ThenBy(item => promisedAtUtc(item))
            .ThenBy(receivedAtUtc)
            .ThenBy(id);

    public static int PriorityPoints(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => LowPriorityPoints,
        ServiceOrderPriority.Normal => NormalPriorityPoints,
        ServiceOrderPriority.High => HighPriorityPoints,
        ServiceOrderPriority.Urgent => UrgentPriorityPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Prioridad desconocida."),
    };

    public static WorkQueue QueueFor(ServiceOrderStatus status) => status switch
    {
        ServiceOrderStatus.AwaitingParts => WorkQueue.AwaitingParts,
        ServiceOrderStatus.ReadyForPickup => WorkQueue.ReadyForPickup,
        _ => WorkQueue.Repair,
    };

    private static RecommendedAction ActionFor(ServiceOrderStatus status, bool hasTechnician) => status switch
    {
        _ when !hasTechnician && status is ServiceOrderStatus.Received => RecommendedAction.AssignTechnician,
        ServiceOrderStatus.Received => RecommendedAction.StartDiagnosis,
        ServiceOrderStatus.Diagnosing => RecommendedAction.CompleteDiagnosis,
        ServiceOrderStatus.AwaitingParts => RecommendedAction.FollowUpParts,
        ServiceOrderStatus.ReadyForPickup => RecommendedAction.DeliverToCustomer,
        _ => RecommendedAction.ContinueRepair,
    };

    /// <summary>Bono por fecha de entrega: solo se aplica el que corresponde, porque son excluyentes.</summary>
    private static ScoreFactor? DueFactor(DateTime? promisedAtUtc, DateTime nowUtc)
    {
        if (promisedAtUtc is not { } promised)
        {
            return null;
        }

        var remaining = promised - nowUtc;
        if (remaining < TimeSpan.Zero)
        {
            return new ScoreFactor("Entrega vencida", OverduePoints);
        }

        if (remaining <= NearDueWindow)
        {
            return new ScoreFactor("Entrega en menos de 24 horas", DueWithin24HoursPoints);
        }

        return remaining <= SoonDueWindow ? new ScoreFactor("Entrega en 24 a 72 horas", DueWithin72HoursPoints) : null;
    }

    private static string PriorityName(ServiceOrderPriority priority) => priority switch
    {
        ServiceOrderPriority.Low => "baja",
        ServiceOrderPriority.Normal => "normal",
        ServiceOrderPriority.High => "alta",
        _ => "urgente",
    };
}
