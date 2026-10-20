using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Domain;

/// <summary>Algoritmo de priorización: puntuación, bonos de fecha excluyentes, antigüedad, listas y desempates.</summary>
public sealed class WorkPrioritizerTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    private static WorkPriorityScore Score(
        ServiceOrderPriority priority = ServiceOrderPriority.Normal,
        ServiceOrderStatus status = ServiceOrderStatus.InRepair,
        double receivedDaysAgo = 0,
        double? promisedInHours = null,
        bool hasTechnician = true) =>
        WorkPrioritizer.Score(
            priority,
            status,
            Now.AddDays(-receivedDaysAgo),
            promisedInHours is { } hours ? Now.AddHours(hours) : null,
            hasTechnician,
            Now);

    [Theory]
    [InlineData(ServiceOrderPriority.Low, 10)]
    [InlineData(ServiceOrderPriority.Normal, 30)]
    [InlineData(ServiceOrderPriority.High, 60)]
    [InlineData(ServiceOrderPriority.Urgent, 90)]
    public void The_configured_priority_sets_the_base_points(ServiceOrderPriority priority, int expected)
    {
        var score = Score(priority);

        Assert.Equal(expected, score.Total);
        Assert.Equal(expected, Assert.Single(score.Factors).Points);
    }

    [Theory]
    [InlineData(-0.01, 50, "Entrega vencida")]
    [InlineData(-200, 50, "Entrega vencida")]
    [InlineData(0, 30, "Entrega en menos de 24 horas")]
    [InlineData(24, 30, "Entrega en menos de 24 horas")]
    [InlineData(24.01, 15, "Entrega en 24 a 72 horas")]
    [InlineData(72, 15, "Entrega en 24 a 72 horas")]
    public void Only_one_due_date_bonus_applies(double promisedInHours, int expectedBonus, string expectedDescription)
    {
        var score = Score(ServiceOrderPriority.Normal, promisedInHours: promisedInHours);

        var dueFactors = score.Factors.Where(factor => factor.Description.StartsWith("Entrega", StringComparison.Ordinal)).ToList();
        var due = Assert.Single(dueFactors);
        Assert.Equal(expectedBonus, due.Points);
        Assert.Equal(expectedDescription, due.Description);
        Assert.Equal(30 + expectedBonus, score.Total);
    }

    [Theory]
    [InlineData(72.01)]
    [InlineData(500.0)]
    [InlineData(null)]
    public void Distant_or_missing_due_dates_add_nothing(double? promisedInHours) =>
        Assert.Equal(30, Score(promisedInHours: promisedInHours).Total);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.99, 0)]
    [InlineData(1, 2)]
    [InlineData(3.9, 6)]
    [InlineData(15, 30)]
    [InlineData(40, 30)]
    public void Age_adds_two_points_per_full_day_up_to_thirty(double days, int expectedPoints) =>
        Assert.Equal(30 + expectedPoints, Score(receivedDaysAgo: days).Total);

    [Fact]
    public void The_age_factor_explains_when_it_reached_its_maximum()
    {
        var score = Score(receivedDaysAgo: 20);

        Assert.Contains(score.Factors, factor => factor is { Description: "20 días en el taller (máximo)", Points: 30 });
    }

    [Fact]
    public void An_urgent_overdue_order_aged_two_weeks_reaches_the_maximum_score()
    {
        var score = Score(ServiceOrderPriority.Urgent, receivedDaysAgo: 16, promisedInHours: -3);

        Assert.Equal(170, score.Total);
        Assert.Equal(["Prioridad urgente", "Entrega vencida", "16 días en el taller (máximo)"], score.Factors.Select(f => f.Description));
        Assert.Equal(score.Total, score.Factors.Sum(f => f.Points));
    }

    [Fact]
    public void A_reception_date_in_the_future_does_not_add_age() =>
        Assert.Equal(30, Score(receivedDaysAgo: -2).Total);

    [Theory]
    [InlineData(ServiceOrderStatus.Received, true, WorkQueue.Repair, RecommendedAction.StartDiagnosis)]
    [InlineData(ServiceOrderStatus.Received, false, WorkQueue.Repair, RecommendedAction.AssignTechnician)]
    [InlineData(ServiceOrderStatus.Diagnosing, true, WorkQueue.Repair, RecommendedAction.CompleteDiagnosis)]
    [InlineData(ServiceOrderStatus.InRepair, true, WorkQueue.Repair, RecommendedAction.ContinueRepair)]
    [InlineData(ServiceOrderStatus.AwaitingParts, true, WorkQueue.AwaitingParts, RecommendedAction.FollowUpParts)]
    [InlineData(ServiceOrderStatus.ReadyForPickup, true, WorkQueue.ReadyForPickup, RecommendedAction.DeliverToCustomer)]
    public void The_status_decides_the_list_and_the_recommended_action(
        ServiceOrderStatus status,
        bool hasTechnician,
        WorkQueue expectedQueue,
        RecommendedAction expectedAction)
    {
        var score = Score(status: status, hasTechnician: hasTechnician);

        Assert.Equal(expectedQueue, score.Queue);
        Assert.Equal(expectedAction, score.Action);
    }

    [Fact]
    public void The_status_does_not_change_the_points()
    {
        var points = Enum.GetValues<ServiceOrderStatus>()
            .Where(ServiceOrderWorkflow.IsOpen)
            .Select(status => Score(ServiceOrderPriority.High, status, receivedDaysAgo: 2, promisedInHours: 10).Total)
            .Distinct();

        Assert.Equal(60 + 30 + 4, Assert.Single(points));
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Delivered)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public void Closed_orders_are_not_prioritized(ServiceOrderStatus status) =>
        Assert.Throws<ArgumentException>(() => Score(status: status));

    [Fact]
    public void Ranking_is_stable_and_breaks_ties_by_due_date_reception_and_number()
    {
        var orders = new[]
        {
            new { Id = 5, Score = 60, Promised = (DateTime?)null, Received = Now.AddDays(-1) },
            new { Id = 4, Score = 60, Promised = (DateTime?)Now.AddDays(2), Received = Now.AddDays(-1) },
            new { Id = 3, Score = 60, Promised = (DateTime?)Now.AddDays(1), Received = Now.AddDays(-1) },
            new { Id = 2, Score = 90, Promised = (DateTime?)null, Received = Now },
            new { Id = 7, Score = 60, Promised = (DateTime?)null, Received = Now.AddDays(-3) },
            new { Id = 6, Score = 60, Promised = (DateTime?)null, Received = Now.AddDays(-1) },
        };

        var ranked = WorkPrioritizer.Rank(orders, o => o.Score, o => o.Promised, o => o.Received, o => o.Id).Select(o => o.Id);

        Assert.Equal([2, 3, 4, 7, 5, 6], ranked);
    }
}
