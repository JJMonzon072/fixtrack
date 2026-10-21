using FixTrack.Application.Reports;

namespace FixTrack.Tests.Application;

/// <summary>Períodos de los reportes: días locales convertidos a intervalos UTC [inicio, fin).</summary>
public sealed class ReportPeriodTests
{
    private static readonly TimeZoneInfo Guatemala = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void A_month_in_Guatemala_starts_at_six_utc_and_ends_at_the_start_of_the_next_day()
    {
        var period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Guatemala);

        Assert.Equal(Utc(2026, 10, 1, 6), period.StartUtc);
        Assert.Equal(Utc(2026, 11, 1, 6), period.EndUtc);
        Assert.Equal(DateTimeKind.Utc, period.StartUtc.Kind);
        Assert.Equal(31, period.Days);
        Assert.Equal(ReportGranularity.Day, period.Granularity);
        Assert.Equal(new DateOnly(2026, 10, 31), period.LocalDate(Utc(2026, 11, 1, 5, 59)));
        Assert.Equal(new DateOnly(2026, 11, 1), period.LocalDate(Utc(2026, 11, 1, 6)));
    }

    [Fact]
    public void A_valid_period_has_no_errors_and_a_full_year_is_allowed()
    {
        Assert.Empty(ReportPeriod.Validate(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21), Today));
        Assert.Empty(ReportPeriod.Validate(new DateOnly(2025, 10, 21), new DateOnly(2026, 10, 20), Today));
        Assert.Empty(ReportPeriod.Validate(Today, Today, Today));
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-09", "To", "igual o posterior")]
    [InlineData("2025-01-01", "2026-10-01", "To", "como máximo 366 días")]
    [InlineData("2026-10-01", "2026-10-22", "To", "posterior a hoy")]
    [InlineData("1999-12-31", "2026-10-01", "From", "anterior al 2000-01-01")]
    [InlineData(null, "2026-10-01", "From", "fecha inicial")]
    [InlineData("2026-10-01", null, "To", "fecha final")]
    public void Invalid_periods_explain_the_problem(string? from, string? to, string key, string message)
    {
        var errors = ReportPeriod.Validate(
            from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
            Today);

        Assert.Contains(message, errors[key], StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_what_validate_rejects()
    {
        Assert.Throws<ArgumentException>(() => ReportPeriod.Create(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1), Guatemala));
        Assert.Throws<ArgumentException>(() => ReportPeriod.Create(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), Guatemala));
    }

    [Theory]
    [InlineData(ReportQuickRange.Last7Days, "2026-10-15")]
    [InlineData(ReportQuickRange.Last30Days, "2026-09-22")]
    [InlineData(ReportQuickRange.Last90Days, "2026-07-24")]
    [InlineData(ReportQuickRange.ThisYear, "2026-01-01")]
    public void Quick_ranges_end_today_and_include_today(ReportQuickRange range, string expectedFrom)
    {
        var period = ReportPeriod.ForQuickRange(range, Today, Guatemala);

        Assert.Equal(DateOnly.Parse(expectedFrom, System.Globalization.CultureInfo.InvariantCulture), period.From);
        Assert.Equal(Today, period.To);
        Assert.True(period.Matches(range, Today));
        Assert.Equal(Utc(2026, 10, 22, 6), period.EndUtc);
    }

    [Theory]
    [InlineData(1, ReportGranularity.Day)]
    [InlineData(31, ReportGranularity.Day)]
    [InlineData(32, ReportGranularity.Week)]
    [InlineData(120, ReportGranularity.Week)]
    [InlineData(121, ReportGranularity.Month)]
    [InlineData(366, ReportGranularity.Month)]
    public void The_series_granularity_depends_on_the_length(int days, ReportGranularity expected)
    {
        var period = ReportPeriod.Create(new DateOnly(2025, 10, 21), new DateOnly(2025, 10, 21).AddDays(days - 1), Guatemala);

        Assert.Equal(expected, period.Granularity);
    }

    [Fact]
    public void Guatemala_has_a_single_offset_segment()
    {
        var period = ReportPeriod.ForQuickRange(ReportQuickRange.ThisYear, Today, Guatemala);

        var segment = Assert.Single(period.OffsetSegments());
        Assert.Equal((period.StartUtc, period.EndUtc, -360), (segment.StartUtc, segment.EndUtc, segment.OffsetMinutes));
    }

    [Fact]
    public void A_daylight_saving_change_splits_the_period_at_the_exact_minute()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var period = ReportPeriod.Create(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), newYork);
        var segments = period.OffsetSegments();

        Assert.Equal(Utc(2026, 3, 1, 5), period.StartUtc);
        Assert.Equal(Utc(2026, 4, 1, 4), period.EndUtc);
        Assert.Equal(
            [(Utc(2026, 3, 1, 5), Utc(2026, 3, 8, 7), -300), (Utc(2026, 3, 8, 7), Utc(2026, 4, 1, 4), -240)],
            segments.Select(segment => (segment.StartUtc, segment.EndUtc, segment.OffsetMinutes)));
        Assert.Equal(new DateOnly(2026, 3, 7), period.LocalDate(Utc(2026, 3, 8, 4, 30)));
        Assert.Equal(new DateOnly(2026, 3, 8), period.LocalDate(Utc(2026, 3, 8, 7, 30)));
    }

    [Fact]
    public void A_day_without_midnight_starts_at_its_first_valid_minute()
    {
        // En La Habana, el 8 de marzo de 2026 el reloj pasa de 23:59 a 01:00: ese día no tiene medianoche.
        Assert.SkipUnless(TimeZoneInfo.TryFindSystemTimeZoneById("America/Havana", out var havana), "Zona America/Havana no disponible.");

        Assert.Equal(Utc(2026, 3, 8, 5), ReportPeriod.StartOfDayUtc(new DateOnly(2026, 3, 8), havana));
        Assert.Equal(Utc(2026, 3, 7, 5), ReportPeriod.StartOfDayUtc(new DateOnly(2026, 3, 7), havana));
    }

    [Fact]
    public void Weekly_series_run_monday_to_sunday_clipped_to_the_period_and_keep_every_order()
    {
        var period = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 15), Guatemala);
        DailyTotal[] received = [new(new DateOnly(2026, 9, 1), 2, 0m), new(new DateOnly(2026, 9, 6), 1, 0m), new(new DateOnly(2026, 9, 7), 4, 0m), new(new DateOnly(2026, 10, 15), 3, 0m)];
        DailyTotal[] delivered = [new(new DateOnly(2026, 9, 30), 1, 120.50m), new(new DateOnly(2026, 10, 4), 2, 99.50m)];

        var series = ReportService.BuildSeries(period, received, delivered);

        Assert.Equal(ReportGranularity.Week, period.Granularity);
        Assert.Equal(
            ["09-01..09-06", "09-07..09-13", "09-14..09-20", "09-21..09-27", "09-28..10-04", "10-05..10-11", "10-12..10-15"],
            series.Select(point => $"{point.Start:MM-dd}..{point.End:MM-dd}"));
        Assert.Equal([3, 4, 0, 0, 0, 0, 3], series.Select(point => point.Received));
        Assert.Equal((3, 220.00m), (series[4].Delivered, series[4].Revenue));
        Assert.Equal(10, series.Sum(point => point.Received));
    }

    [Fact]
    public void Monthly_series_cover_partial_first_and_last_months_and_merge_split_days()
    {
        var period = ReportPeriod.Create(new DateOnly(2026, 1, 15), Today, Guatemala);
        // Un día puede llegar en dos filas (dos tramos de desfase): se suman.
        DailyTotal[] received = [new(new DateOnly(2026, 2, 10), 1, 0m), new(new DateOnly(2026, 2, 10), 2, 0m)];

        var series = ReportService.BuildSeries(period, received, []);

        Assert.Equal(10, series.Count);
        Assert.Equal((new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)), (series[0].Start, series[0].End));
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 3), (series[1].Start, series[1].End, series[1].Received));
        Assert.Equal((new DateOnly(2026, 10, 1), Today), (series[^1].Start, series[^1].End));
    }
}
