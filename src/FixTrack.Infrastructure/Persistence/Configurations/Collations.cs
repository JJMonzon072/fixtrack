namespace FixTrack.Infrastructure.Persistence.Configurations;

internal static class Collations
{
    /// <summary>
    /// Español moderno, sin distinguir mayúsculas ni tildes: "jose" encuentra "José" y la ñ se ordena después de la n.
    /// Disponible en todas las ediciones de SQL Server, incluida Express.
    /// </summary>
    public const string SpanishAccentInsensitive = "Modern_Spanish_100_CI_AI";
}
