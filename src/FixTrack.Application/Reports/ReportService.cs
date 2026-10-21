using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Application.Reports;

internal sealed class ReportService(IReportRepository reports, TimeProvider clock) : IReportService
{
    internal const string UnassignedName = "Sin técnico asignado";

    public async Task<OperationsReport> GetAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Consultas secuenciales: comparten el mismo contexto de EF Core. Su cantidad es fija.
        var received = await reports.CountReceivedAsync(filter, cancellationToken);
        var delivered = await reports.SumDeliveredAsync(filter, cancellationToken);
        var ready = await reports.SumTimeToReadyAsync(filter, cancellationToken);
        var cancelled = await reports.CountCancelledAsync(filter, cancellationToken);
        var receivedDaily = await reports.CountReceivedByDayAsync(filter, cancellationToken);
        var deliveredDaily = await reports.SumDeliveredByDayAsync(filter, cancellationToken);
        var activity = await reports.ListTechnicianActivityAsync(filter, cancellationToken);
        var names = activity.Any(row => row.TechnicianId is not null)
            ? await reports.ListTechnicianNamesAsync(cancellationToken)
            : [];

        var receivedTotal = received.Sum(group => group.Count);
        var deliveredTotal = delivered.Sum(group => group.Count);
        var revenue = delivered.Sum(group => group.FinalCostSum);

        return new OperationsReport(
            filter,
            receivedTotal,
            deliveredTotal,
            cancelled,
            DeliveredWithPromise: delivered.Sum(group => group.WithPromise),
            DeliveredLate: delivered.Sum(group => group.Late),
            AverageHoursToDelivery: ReportMath.AverageHours(delivered.Sum(group => group.SecondsToDeliverySum), deliveredTotal),
            AverageHoursToReady: ReportMath.AverageHours(ready.SecondsSum, ready.Count),
            ReadyMeasured: ready.Count,
            Revenue: revenue,
            AverageFinalCost: deliveredTotal == 0 ? null : Math.Round(revenue / deliveredTotal, 2, MidpointRounding.AwayFromZero),
            DeliveredWithEstimate: delivered.Sum(group => group.WithEstimate),
            EstimatedCostSum: delivered.Sum(group => group.EstimatedCostSum),
            FinalCostSumWithEstimate: delivered.Sum(group => group.FinalCostSumWithEstimate),
            ReceivedByStatus:
            [
                .. Enum.GetValues<ServiceOrderStatus>().Select(status => new ReportShare<ServiceOrderStatus>(
                    status,
                    received.Where(group => group.Status == status).Sum(group => group.Count),
                    receivedTotal)),
            ],
            Priorities: [.. Enum.GetValues<ServiceOrderPriority>().Reverse().Select(priority => Compare(priority, received, delivered))],
            Series: BuildSeries(filter.Period, receivedDaily, deliveredDaily),
            Technicians: Technicians(activity, names),
            CalculatedAtUtc: clock.GetUtcNow().UtcDateTime);
    }

    public async Task<ReportExport> ExportAsync(ReportFilter filter, ReportExportBasis basis, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);

        var rows = await reports.ListForExportAsync(filter, basis, maxRows + 1, cancellationToken);
        return rows.Count > maxRows ? new ReportExport([], TooLarge: true, maxRows) : new ReportExport(rows, TooLarge: false, maxRows);
    }

    /// <summary>
    /// Agrupa los totales diarios en tramos de un día, una semana (de lunes a domingo) o un mes, recortados al período.
    /// Los tramos sin órdenes aparecen con cero: la serie es continua.
    /// </summary>
    internal static IReadOnlyList<ReportSeriesPoint> BuildSeries(
        ReportPeriod period,
        IReadOnlyList<DailyTotal> receivedDaily,
        IReadOnlyList<DailyTotal> deliveredDaily)
    {
        // Un día puede llegar en dos filas si un cambio de horario lo divide en dos tramos de desfase.
        var received = receivedDaily.GroupBy(day => day.Day).ToDictionary(group => group.Key, group => group.Sum(day => day.Count));
        var delivered = deliveredDaily.GroupBy(day => day.Day)
            .ToDictionary(group => group.Key, group => (Count: group.Sum(day => day.Count), Amount: group.Sum(day => day.Amount)));

        var points = new List<ReportSeriesPoint>();
        for (var start = period.From; start <= period.To;)
        {
            var end = BucketEnd(start, period.Granularity);
            if (end > period.To)
            {
                end = period.To;
            }

            int receivedCount = 0, deliveredCount = 0;
            var revenue = 0m;
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                receivedCount += received.GetValueOrDefault(day);
                if (delivered.TryGetValue(day, out var totals))
                {
                    deliveredCount += totals.Count;
                    revenue += totals.Amount;
                }
            }

            points.Add(new ReportSeriesPoint(start, end, receivedCount, deliveredCount, revenue));
            start = end.AddDays(1);
        }

        return points;
    }

    private static DateOnly BucketEnd(DateOnly start, ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Day => start,
        ReportGranularity.Week => start.AddDays(6 - (((int)start.DayOfWeek + 6) % 7)),
        _ => new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)),
    };

    private static PriorityComparison Compare(
        ServiceOrderPriority priority,
        IReadOnlyList<ReceivedGroup> received,
        IReadOnlyList<DeliveredGroup> delivered)
    {
        var deliveries = delivered.FirstOrDefault(group => group.Priority == priority);
        return new PriorityComparison(
            priority,
            received.Where(group => group.Priority == priority).Sum(group => group.Count),
            deliveries?.Count ?? 0,
            deliveries?.Late ?? 0,
            deliveries?.WithPromise ?? 0,
            deliveries is null ? null : ReportMath.AverageHours(deliveries.SecondsToDeliverySum, deliveries.Count));
    }

    private static IReadOnlyList<TechnicianActivity> Technicians(
        IReadOnlyList<TechnicianActivityRow> activity,
        IReadOnlyList<TechnicianName> names)
    {
        var byId = names.ToDictionary(name => name.Id);
        return
        [
            .. activity
                .Where(row => row.Received + row.Delivered + row.OpenNow > 0)
                .Select(row =>
                {
                    var name = row.TechnicianId is { } id && byId.TryGetValue(id, out var technician) ? technician : null;
                    return new TechnicianActivity(
                        row.TechnicianId,
                        name?.FullName ?? UnassignedName,
                        name?.IsActive ?? true,
                        row.Received,
                        row.Delivered,
                        row.Late,
                        row.FinalCostSum,
                        ReportMath.AverageHours(row.SecondsToDeliverySum, row.Delivered),
                        row.OpenNow);
                })
                .OrderBy(row => row.TechnicianId is null)
                .ThenByDescending(row => row.Delivered)
                .ThenByDescending(row => row.Received)
                .ThenBy(row => row.Name, StringComparer.CurrentCulture),
        ];
    }
}
