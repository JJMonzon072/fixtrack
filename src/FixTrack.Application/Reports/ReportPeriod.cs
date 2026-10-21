namespace FixTrack.Application.Reports;

/// <summary>Filtros rápidos del centro de reportes. Todos terminan hoy (incluido).</summary>
public enum ReportQuickRange
{
    Last7Days,
    Last30Days,
    Last90Days,
    ThisYear,
}

/// <summary>Agrupación de las series según la duración del período.</summary>
public enum ReportGranularity
{
    Day,
    Week,
    Month,
}

/// <summary>Tramo del período en el que la zona horaria mantiene el mismo desfase con UTC.</summary>
/// <param name="OffsetMinutes">Minutos que se suman a una fecha UTC para obtener la hora local.</param>
public sealed record ReportOffsetSegment(DateTime StartUtc, DateTime EndUtc, int OffsetMinutes);

/// <summary>
/// Período de un reporte expresado en días del calendario local del taller, del día <see cref="From"/> al día
/// <see cref="To"/>, ambos incluidos. En la base (fechas en UTC) equivale al intervalo
/// [<see cref="StartUtc"/>, <see cref="EndUtc"/>): desde el inicio del primer día local, incluido, hasta el inicio del
/// día siguiente al último, excluido. Así una orden recibida a las 23:59 del último día cuenta y una recibida a las
/// 00:00 del día siguiente no, sin depender de la precisión de las fechas.
/// <para>
/// La zona es la del servidor (<see cref="TimeProvider.LocalTimeZone"/>), la misma con la que FixTrack muestra todas las
/// fechas: America/Guatemala en la imagen de producción. Si la zona tuviera horario de verano, un día podría durar 23 o
/// 25 horas: los límites se calculan día por día y las series se agrupan por tramos de desfase constante.
/// </para>
/// </summary>
public sealed record ReportPeriod
{
    /// <summary>Duración máxima de un período: un año, también bisiesto. Limita el costo de las consultas.</summary>
    public const int MaxDays = 366;

    /// <summary>Días hasta los que las series se agrupan por día; después, por semana.</summary>
    public const int MaxDailyBuckets = 31;

    /// <summary>Días hasta los que las series se agrupan por semana; después, por mes.</summary>
    public const int MaxWeeklyDays = 120;

    public static readonly DateOnly MinDate = new(2000, 1, 1);

    private ReportPeriod(DateOnly from, DateOnly to, TimeZoneInfo timeZone)
    {
        From = from;
        To = to;
        TimeZone = timeZone;
        StartUtc = StartOfDayUtc(from, timeZone);
        EndUtc = StartOfDayUtc(to.AddDays(1), timeZone);
    }

    /// <summary>Primer día del período (incluido), en el calendario local.</summary>
    public DateOnly From { get; }

    /// <summary>Último día del período (incluido), en el calendario local.</summary>
    public DateOnly To { get; }

    public TimeZoneInfo TimeZone { get; }

    /// <summary>Inicio del primer día local, en UTC (incluido).</summary>
    public DateTime StartUtc { get; }

    /// <summary>Inicio del día siguiente al último, en UTC (excluido).</summary>
    public DateTime EndUtc { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    public ReportGranularity Granularity => Days switch
    {
        <= MaxDailyBuckets => ReportGranularity.Day,
        <= MaxWeeklyDays => ReportGranularity.Week,
        _ => ReportGranularity.Month,
    };

    /// <summary>Crea el período; los datos deben ser válidos según <see cref="Validate"/>.</summary>
    public static ReportPeriod Create(DateOnly from, DateOnly to, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDays || from < MinDate)
        {
            throw new ArgumentException("El período no es válido: revisa las fechas con ReportPeriod.Validate.");
        }

        return new ReportPeriod(from, to, timeZone);
    }

    /// <summary>
    /// Errores de un período pedido, con las claves <c>From</c> y <c>To</c>. <paramref name="today"/> es la fecha local
    /// actual: un período no puede terminar después de hoy.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        if (from is null)
        {
            errors[nameof(From)] = "Indica la fecha inicial con el formato AAAA-MM-DD.";
        }
        else if (from < MinDate)
        {
            errors[nameof(From)] = $"La fecha inicial no puede ser anterior al {MinDate:yyyy-MM-dd}.";
        }

        if (to is null)
        {
            errors[nameof(To)] = "Indica la fecha final con el formato AAAA-MM-DD.";
        }
        else if (to > today)
        {
            errors[nameof(To)] = "La fecha final no puede ser posterior a hoy.";
        }

        if (errors.Count == 0)
        {
            if (to < from)
            {
                errors[nameof(To)] = "La fecha final debe ser igual o posterior a la inicial.";
            }
            else if (to!.Value.DayNumber - from!.Value.DayNumber + 1 > MaxDays)
            {
                errors[nameof(To)] = $"El período puede abarcar como máximo {MaxDays} días.";
            }
        }

        return errors;
    }

    /// <summary>Período de un filtro rápido que termina hoy.</summary>
    public static ReportPeriod ForQuickRange(ReportQuickRange range, DateOnly today, TimeZoneInfo timeZone) => range switch
    {
        ReportQuickRange.Last7Days => Create(today.AddDays(-6), today, timeZone),
        ReportQuickRange.Last90Days => Create(today.AddDays(-89), today, timeZone),
        ReportQuickRange.ThisYear => Create(new DateOnly(today.Year, 1, 1), today, timeZone),
        _ => Create(today.AddDays(-29), today, timeZone),
    };

    /// <summary>Fecha local actual según el reloj y la zona indicados.</summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, clock.LocalTimeZone));

    /// <summary>¿Coincide con un filtro rápido para la fecha <paramref name="today"/>?</summary>
    public bool Matches(ReportQuickRange range, DateOnly today) =>
        ForQuickRange(range, today, TimeZone) is var quick && quick.From == From && quick.To == To;

    /// <summary>Día local al que pertenece un instante UTC.</summary>
    public DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone));

    /// <summary>
    /// Tramos consecutivos del período con un desfase constante. Sin horario de verano (como en Guatemala) hay uno solo:
    /// la base puede agrupar por día local sumando el desfase a las fechas UTC.
    /// </summary>
    public IReadOnlyList<ReportOffsetSegment> OffsetSegments()
    {
        var segments = new List<ReportOffsetSegment>();
        var start = StartUtc;
        var offset = OffsetAt(start);
        for (var probe = start.AddHours(1); probe < EndUtc; probe = probe.AddHours(1))
        {
            if (OffsetAt(probe) == offset)
            {
                continue;
            }

            // El cambio ocurrió en la última hora: se busca el minuto exacto.
            var low = probe.AddHours(-1);
            var high = probe;
            while ((high - low).TotalMinutes > 1)
            {
                var middle = low.AddMinutes(Math.Floor((high - low).TotalMinutes / 2));
                if (OffsetAt(middle) == offset)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            segments.Add(new ReportOffsetSegment(start, high, offset));
            start = high;
            offset = OffsetAt(high);
        }

        segments.Add(new ReportOffsetSegment(start, EndUtc, offset));
        return segments;
    }

    /// <summary>
    /// Inicio de un día local en UTC: la medianoche, o el primer minuto válido si ese día la medianoche no existe por un
    /// cambio de horario. Si la medianoche se repite, se toma la primera.
    /// </summary>
    internal static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 24 * 60; minutes++)
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var earliest = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - earliest, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private int OffsetAt(DateTime utc) => (int)TimeZone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).TotalMinutes;
}
