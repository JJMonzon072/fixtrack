namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Patrones para búsquedas con LIKE. El texto del usuario se escapa para que %, _ y [ se busquen
/// literalmente, y siempre se envía como parámetro.
/// </summary>
internal static class LikePatterns
{
    public const string Escape = "\\";

    public static string Contains(string text) =>
        "%" + text
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal)
            .Replace("[", Escape + "[", StringComparison.Ordinal) + "%";
}
