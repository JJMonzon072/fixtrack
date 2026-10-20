namespace FixTrack.Application.Common;

/// <summary>
/// La base de datos rechazó un valor que debe ser único (por ejemplo, un correo ya registrado).
/// La lanza la infraestructura para que la aplicación no dependa del proveedor de datos.
/// </summary>
public sealed class DuplicateValueException(string message, Exception innerException)
    : Exception(message, innerException);
