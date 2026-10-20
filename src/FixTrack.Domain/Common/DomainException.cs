namespace FixTrack.Domain.Common;

/// <summary>Se lanza cuando una operación violaría una regla del dominio.</summary>
public sealed class DomainException(string message) : Exception(message);
