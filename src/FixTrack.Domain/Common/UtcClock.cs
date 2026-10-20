namespace FixTrack.Domain.Common;

/// <summary>Garantiza que todas las fechas del dominio se manejen en UTC.</summary>
internal static class UtcClock
{
    public static DateTime Ensure(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("La fecha debe expresarse en UTC.", parameterName);
}
