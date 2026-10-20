namespace FixTrack.Application.Common;

/// <summary>
/// Otra solicitud modificó el registro entre la lectura y el guardado (concurrencia optimista).
/// La lanza la infraestructura para que la aplicación no dependa del proveedor de datos.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception innerException)
    : Exception(message, innerException);
