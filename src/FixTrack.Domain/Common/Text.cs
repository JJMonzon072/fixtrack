using System.Text.RegularExpressions;

namespace FixTrack.Domain.Common;

/// <summary>Normalización de texto capturado por los usuarios.</summary>
public static partial class Text
{
    /// <summary>Quita espacios al inicio y al final y reduce los espacios internos a uno solo.</summary>
    public static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Whitespace().Replace(value.Trim(), " ");

    /// <summary>Igual que <see cref="Clean"/>, pero devuelve <c>null</c> si el texto queda vacío.</summary>
    public static string? CleanOrNull(string? value)
    {
        var cleaned = Clean(value);
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Limpia texto multilínea: conserva los saltos de línea y quita espacios sobrantes.</summary>
    public static string? CleanMultilineOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lines = value.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim());
        var cleaned = MultipleBlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLines();
}
