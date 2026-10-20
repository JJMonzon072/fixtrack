using System.Globalization;

namespace FixTrack.Web.Services;

/// <summary>
/// Formatea en español las fechas guardadas en UTC, convertidas a la zona horaria del servidor.
/// En un servidor Linux la zona se define con la variable de entorno TZ (por ejemplo, America/Guatemala).
/// </summary>
public sealed class DateDisplay(TimeProvider clock)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    /// <summary>Fecha corta, por ejemplo "19 oct 2026".</summary>
    public string Short(DateTime utc) => ToLocal(utc).ToString("d MMM yyyy", Spanish).Replace(".", string.Empty);

    /// <summary>Fecha y hora, por ejemplo "19 de octubre de 2026, 21:15".</summary>
    public string Long(DateTime utc) => ToLocal(utc).ToString("d 'de' MMMM 'de' yyyy, HH:mm", Spanish);

    /// <summary>Valor ISO 8601 en UTC para el atributo datetime de &lt;time&gt;.</summary>
    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone);
}
