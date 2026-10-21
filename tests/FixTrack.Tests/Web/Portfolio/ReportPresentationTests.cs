using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Reports;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>Parámetros del centro de reportes, geometría de los gráficos y formatos de los indicadores.</summary>
public sealed class ReportPresentationTests
{
    private static readonly TimeZoneInfo Guatemala = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static ReportFilter? Parse(ReportQuery query, out IReadOnlyDictionary<string, string> errors) =>
        query.TryCreateFilter(Today, Guatemala, id => id == 7, out errors);

    [Fact]
    public void Without_parameters_the_report_covers_the_last_thirty_days()
    {
        var filter = Parse(new ReportQuery(null, null, null, null, null, null), out var errors);

        Assert.Empty(errors);
        Assert.Equal((new DateOnly(2026, 9, 22), Today), (filter!.Period.From, filter.Period.To));
        Assert.Equal((null, null, null, false), (filter.Status, filter.Priority, filter.TechnicianId, filter.UnassignedOnly));
    }

    [Fact]
    public void Dates_take_precedence_over_the_quick_range_and_filters_are_parsed()
    {
        var filter = Parse(new ReportQuery("7d", "2026-10-01", "2026-10-15", "en-reparacion", "urgente", "7"), out var errors);
        var unassigned = Parse(new ReportQuery("anio", null, null, null, null, "SIN-ASIGNAR"), out _);

        Assert.Empty(errors);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)), (filter!.Period.From, filter.Period.To));
        Assert.Equal((ServiceOrderStatus.InRepair, ServiceOrderPriority.Urgent, 7), (filter.Status, filter.Priority, filter.TechnicianId));
        Assert.Equal((new DateOnly(2026, 1, 1), true, (int?)null), (unassigned!.Period.From, unassigned.UnassignedOnly, unassigned.TechnicianId));
    }

    [Theory]
    [InlineData("semana", null, null, null, null, null, "rango")]
    [InlineData(null, "01/10/2026", "2026-10-15", null, null, null, "desde")]
    [InlineData(null, "2026-10-01", null, null, null, null, "hasta")]
    [InlineData(null, "2026-10-15", "2026-10-01", null, null, null, "hasta")]
    [InlineData(null, "2026-10-01", "2026-12-01", null, null, null, "hasta")]
    [InlineData(null, "2024-01-01", "2026-10-01", null, null, null, "hasta")]
    [InlineData(null, null, null, "perdida", null, null, "estado")]
    [InlineData(null, null, null, null, "maxima", null, "prioridad")]
    [InlineData(null, null, null, null, null, "99", "tecnico")]
    [InlineData(null, null, null, null, null, "-7", "tecnico")]
    [InlineData(null, null, null, null, null, "1 OR 1=1", "tecnico")]
    public void Invalid_parameters_produce_an_error_and_no_filter(
        string? range, string? from, string? to, string? status, string? priority, string? technician, string key)
    {
        var filter = Parse(new ReportQuery(range, from, to, status, priority, technician), out var errors);

        Assert.Null(filter);
        Assert.True(errors.ContainsKey(key), string.Join(", ", errors.Keys));
    }

    [Theory]
    [InlineData(0, true, 4)]
    [InlineData(3, true, 4)]
    [InlineData(7, true, 8)]
    [InlineData(17, true, 20)]
    [InlineData(13, true, 16)]
    [InlineData(0, false, 100)]
    [InlineData(1420, false, 1600)]
    [InlineData(520, false, 600)]
    [InlineData(36245, false, 40000)]
    public void Axes_end_at_round_values(double max, bool wholeNumbers, double expected) =>
        Assert.Equal(expected, ReportCharts.NiceMax(max, wholeNumbers));

    [Fact]
    public void The_volume_chart_has_two_bars_per_bucket_and_thins_labels_on_narrow_screens()
    {
        var period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Guatemala);
        var series = Enumerable.Range(0, 31)
            .Select(day => new ReportSeriesPoint(period.From.AddDays(day), period.From.AddDays(day), day % 3, day == 4 ? 7 : 0, 0m))
            .ToList();

        var wide = ReportCharts.Volume(series, ReportGranularity.Day, ChartSize.Wide);
        var narrow = ReportCharts.Volume(series, ReportGranularity.Day, ChartSize.Narrow);

        Assert.Equal(62, wide.Bars.Count);
        Assert.All(wide.Bars, bar => Assert.InRange(bar.Y + bar.Height, 0, ChartSize.Wide.Baseline + 0.001));
        Assert.Equal(0, wide.Bars.Single(bar => bar.Series == 0 && bar.Title.StartsWith("1 oct 2026", StringComparison.Ordinal)).Height);
        Assert.Equal("5 oct 2026: 7 entregadas", wide.Bars.Single(bar => bar.Series == 1 && bar.Height > 0).Title);
        Assert.Equal(["0", "2", "4", "6", "8"], wide.Ticks.Select(tick => tick.Label));
        Assert.InRange(wide.Labels.Count, 1, ChartSize.Wide.MaxXLabels);
        Assert.InRange(narrow.Labels.Count, 1, ChartSize.Narrow.MaxXLabels);
        Assert.Equal("1 oct", narrow.Labels[0].Text);
    }

    [Fact]
    public void The_revenue_chart_draws_a_line_and_an_area_closed_on_the_baseline()
    {
        ReportSeriesPoint[] series = [new(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 1, 1, 520m), new(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 0, 0, 0m)];

        var chart = ReportCharts.Revenue(series, ReportGranularity.Month, ChartSize.Wide);

        Assert.StartsWith("M", chart.LinePath, StringComparison.Ordinal);
        Assert.EndsWith("Z", chart.AreaPath, StringComparison.Ordinal);
        Assert.Equal(["Q 0", "Q 150", "Q 300", "Q 450", "Q 600"], chart.Ticks.Select(tick => tick.Label));
        Assert.Equal("enero 2026: Q 520.00", chart.Points[0].Title);
        Assert.Equal(ChartSize.Wide.Baseline, chart.Points[1].Y);
    }

    [Theory]
    [InlineData(null, "Sin datos")]
    [InlineData(18.25, "18.3 h")]
    [InlineData(111.0, "4.6 días")]
    public void Durations_are_readable(double? hours, string expected) => Assert.Equal(expected, ReportsViewModel.Hours(hours));

    [Fact]
    public void Variances_show_their_sign()
    {
        Assert.Equal("+1.7 %", ReportsViewModel.VariancePercent(20m, 1.7m));
        Assert.Equal("−2.5 %", ReportsViewModel.VariancePercent(-30m, -2.5m));
        Assert.Equal("Sin datos", ReportsViewModel.VariancePercent(null, null));
        Assert.Equal("+Q 1,095.00", ReportsViewModel.VarianceAmount(1095m));
        Assert.Equal("−Q 30.00", ReportsViewModel.VarianceAmount(-30m));
    }
}
