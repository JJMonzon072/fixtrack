using FixTrack.Tests.Application;
using FixTrack.Web.Models;
using FixTrack.Web.Services;

namespace FixTrack.Tests.Web;

/// <summary>Montos, fechas y parámetros de la dirección tal como los interpreta la interfaz.</summary>
public sealed class PresentationTests
{
    private static readonly TimeZoneInfo Guatemala = TimeZoneInfo.CreateCustomTimeZone("GT", TimeSpan.FromHours(-6), "Guatemala", "Guatemala");

    [Theory]
    [InlineData("1250", "1250")]
    [InlineData("1250.5", "1250.5")]
    [InlineData(" Q 1,250.50 ", "1250.50")]
    [InlineData("1250,50", "1250.50")]
    [InlineData("1,250", "1250")]
    [InlineData("12,345,678.90", "12345678.90")]
    [InlineData("-5", "-5")]
    [InlineData("10.555", "10.555")]
    public void Money_accepts_common_ways_of_writing_quetzales(string text, string expected)
    {
        Assert.True(Money.TryParse(text, out var amount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData("doscientos")]
    [InlineData("1.250,50")]
    [InlineData("12,34,5")]
    [InlineData("1e5")]
    [InlineData("Q")]
    public void Money_rejects_text_that_is_not_an_amount(string text) =>
        Assert.False(Money.TryParse(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void An_empty_amount_is_null(string? text)
    {
        Assert.True(Money.TryParse(text, out var amount));
        Assert.Null(amount);
    }

    [Theory]
    [InlineData(1250.5, "Q 1,250.50")]
    [InlineData(0, "Q 0.00")]
    [InlineData(-30, "−Q 30.00")]
    public void Money_is_shown_in_quetzales(decimal amount, string expected) =>
        Assert.Equal(expected, Money.Format(amount));

    [Fact]
    public void A_local_date_input_is_converted_to_utc_with_the_workshop_time_zone()
    {
        var dates = new DateDisplay(new ZonedClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), Guatemala));

        Assert.True(dates.TryParseInput("2026-10-21T17:00", out var utc));

        Assert.Equal(new DateTime(2026, 10, 21, 23, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal("2026-10-21T17:00", dates.InputValue(utc));
        Assert.Equal("17:00", dates.Time(utc!.Value));
    }

    [Theory]
    [InlineData("21/10/2026")]
    [InlineData("2026-13-01T10:00")]
    [InlineData("mañana")]
    public void Invalid_date_inputs_are_rejected(string value)
    {
        var dates = new DateDisplay(new FixedClock(DateTimeOffset.UtcNow));

        Assert.False(dates.TryParseInput(value, out _));
    }

    [Theory]
    [InlineData(0.5, "30 minutos")]
    [InlineData(1, "1 hora")]
    [InlineData(5, "5 horas")]
    [InlineData(26, "1 día")]
    [InlineData(-72, "3 días")]
    public void Durations_are_written_in_words(double hours, string expected) =>
        Assert.Equal(expected, DateDisplay.Duration(TimeSpan.FromHours(hours)));

    [Fact]
    public void Relative_days_use_the_local_calendar()
    {
        // 20 de octubre, 23:30 en Guatemala (05:30 UTC del día 21).
        var dates = new DateDisplay(new ZonedClock(new DateTimeOffset(2026, 10, 21, 5, 30, 0, TimeSpan.Zero), Guatemala));

        Assert.Equal("hoy", dates.RelativeDay(new DateTime(2026, 10, 21, 5, 45, 0, DateTimeKind.Utc)));
        Assert.Equal("mañana", dates.RelativeDay(new DateTime(2026, 10, 21, 7, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("ayer", dates.RelativeDay(new DateTime(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc)));
        Assert.Null(dates.RelativeDay(new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)));
    }

    private sealed class ZonedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
