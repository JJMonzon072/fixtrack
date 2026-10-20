using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FixTrack.Infrastructure.Persistence.Resilience;

/// <summary>
/// Disyuntor sencillo para la conexión a SQL Server. Después de <see cref="FailureThreshold"/> fallos de conexión
/// seguidos, durante <see cref="BreakDuration"/> no se intenta conectar: las solicitudes fallan de inmediato con
/// <see cref="Application.Common.DatabaseUnavailableException"/>, en lugar de esperar el tiempo de conexión y los
/// reintentos de cada una (lo que multiplicaría los intentos contra una base caída). Pasado ese tiempo, la siguiente
/// solicitud vuelve a probar; una conexión correcta lo restablece.
/// <para>Registra el detalle del error una sola vez por caída (al abrirse por primera vez) y una línea cuando la base
/// vuelve a responder: durante la caída, cada solicitud rechazada deja solo una línea breve (ver el manejador de 503).</para>
/// </summary>
public sealed class DatabaseAvailability(TimeProvider clock, ILogger<DatabaseAvailability> logger)
{
    public const int FailureThreshold = 3;
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(15);

    // Errores de SqlClient que indican que el servidor no responde o no es alcanzable (no errores de la consulta).
    private static readonly HashSet<int> ConnectionErrors = [-2, 2, 53, 64, 121, 233, 258, 4060, 10053, 10054, 10060, 10061, 11001, 40613];

    private readonly Lock _lock = new();
    private int _failures;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;
    private DateTimeOffset? _outageSince;

    public DatabaseAvailability(TimeProvider clock)
        : this(clock, NullLogger<DatabaseAvailability>.Instance)
    {
    }

    /// <summary>¿Está abierto el disyuntor? Si lo está, cuánto falta para volver a probar.</summary>
    public bool IsOpen(out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            retryAfter = _openUntil - clock.GetUtcNow();
            if (retryAfter > TimeSpan.Zero)
            {
                return true;
            }

            retryAfter = TimeSpan.Zero;
            return false;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failures = 0;
            if (_outageSince is { } since)
            {
                _outageSince = null;
                logger.LogInformation("La base de datos volvió a responder después de {Seconds:0} s.", (clock.GetUtcNow() - since).TotalSeconds);
            }
        }
    }

    /// <summary>Cuenta un fallo de conexión si el error indica que el servidor no responde.</summary>
    public void RecordFailure(Exception exception)
    {
        if (!IsConnectionFailure(exception))
        {
            return;
        }

        lock (_lock)
        {
            if (++_failures < FailureThreshold)
            {
                return;
            }

            var now = clock.GetUtcNow();
            _openUntil = now + BreakDuration;
            _failures = 0;
            if (_outageSince is null)
            {
                _outageSince = now;
                logger.LogWarning(
                    exception,
                    "La base de datos no responde ({Failures} fallos de conexión seguidos). Las solicitudes reciben 503 sin intentar conectar y se vuelve a probar cada {Seconds} s.",
                    FailureThreshold,
                    BreakDuration.TotalSeconds);
            }
        }
    }

    public static bool IsConnectionFailure(Exception exception) => exception switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(error => ConnectionErrors.Contains(error.Number)),
        TimeoutException => true,
        _ => exception.InnerException is { } inner && IsConnectionFailure(inner),
    };
}
