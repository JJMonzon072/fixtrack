using System.Globalization;

namespace FixTrack.Web.Services;

/// <summary>
/// Formatea en español las fechas guardadas en UTC, convertidas a la zona horaria del servidor.
/// En un servidor Linux la zona se define con la variable de entorno TZ (por ejemplo, America/Guatemala).
/// </summary>
public sealed class DateDisplay(TimeProvider clock)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly string[] InputFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    /// <summary>Fecha corta, por ejemplo "19 oct 2026".</summary>
    public string Short(DateTime utc) => ToLocal(utc).ToString("d MMM yyyy", Spanish).Replace(".", string.Empty);

    /// <summary>Fecha y hora, por ejemplo "19 de octubre de 2026, 21:15".</summary>
    public string Long(DateTime utc) => ToLocal(utc).ToString("d 'de' MMMM 'de' yyyy, HH:mm", Spanish);

    /// <summary>Fecha corta con hora, por ejemplo "21 oct 2026, 17:00".</summary>
    public string ShortWithTime(DateTime utc) => $"{Short(utc)}, {Time(utc)}";

    /// <summary>Hora local, por ejemplo "17:00".</summary>
    public string Time(DateTime utc) => ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Valor ISO 8601 en UTC para el atributo datetime de &lt;time&gt;.</summary>
    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Valor de un campo <c>datetime-local</c> en la hora local, por ejemplo "2026-10-21T17:00".</summary>
    public string InputValue(DateTime? utc) =>
        utc is { } value ? ToLocal(value).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// Interpreta el valor de un campo <c>datetime-local</c> como hora local y lo convierte a UTC.
    /// Un valor vacío es <c>null</c>; devuelve <c>false</c> si el texto no es una fecha y hora válidas.
    /// </summary>
    public bool TryParseInput(string? value, out DateTime? utc)
    {
        utc = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateTime.TryParseExact(value.Trim(), InputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        var zone = clock.LocalTimeZone;
        if (zone.IsInvalidTime(local))
        {
            return false;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
        return true;
    }

    /// <summary>Duración aproximada en palabras, por ejemplo "3 días", "5 horas" o "20 minutos".</summary>
    public static string Duration(TimeSpan span)
    {
        var value = span.Duration();
        return value.TotalDays >= 1 ? Plural((int)value.TotalDays, "día", "días")
            : value.TotalHours >= 1 ? Plural((int)value.TotalHours, "hora", "horas")
            : Plural(Math.Max(1, (int)value.TotalMinutes), "minuto", "minutos");
    }

    /// <summary>Día relativo a hoy en la hora local: "hoy", "mañana", "ayer" o <c>null</c> si es otro día.</summary>
    public string? RelativeDay(DateTime utc)
    {
        var days = (ToLocal(utc).Date - ToLocal(clock.GetUtcNow().UtcDateTime).Date).Days;
        return days switch
        {
            0 => "hoy",
            1 => "mañana",
            -1 => "ayer",
            _ => null,
        };
    }

    private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    private DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone);
}
