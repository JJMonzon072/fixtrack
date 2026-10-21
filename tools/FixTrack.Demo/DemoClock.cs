namespace FixTrack.Demo;

/// <summary>
/// Reloj de la carga de datos: cada operación se ejecuta en la fecha del escenario, de modo que el historial, la
/// auditoría y los reportes tienen fechas creíbles. Las reglas del dominio se siguen aplicando con esa fecha (por
/// ejemplo, una nueva entrega comprometida no puede quedar en el pasado del momento en que se cambia).
/// </summary>
internal sealed class DemoClock(TimeZoneInfo localTimeZone) : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Fija el instante de la próxima operación. Nunca acepta una fecha futura.</summary>
    public void Set(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha debe estar en UTC.", nameof(utc));
        }

        if (utc > DateTime.UtcNow)
        {
            throw new InvalidOperationException($"El escenario no puede registrar operaciones en el futuro ({utc:O}).");
        }

        _now = new DateTimeOffset(utc);
    }
}
