namespace FixTrack.Application.Common;

/// <summary>
/// La base de datos no está disponible en este momento (por ejemplo, Somee no responde). La interfaz la muestra como
/// "servicio temporalmente no disponible" (503), sin detalles técnicos.
/// </summary>
public sealed class DatabaseUnavailableException(string message, TimeSpan retryAfter, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Tiempo recomendado antes de volver a intentar.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}
