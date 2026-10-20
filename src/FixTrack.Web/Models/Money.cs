using System.Globalization;
using System.Text.RegularExpressions;

namespace FixTrack.Web.Models;

/// <summary>
/// Montos en quetzales. Se muestran como "Q 1,250.00" y se capturan como texto para no depender de la
/// cultura de la solicitud: en español "1.250" significaría mil doscientos cincuenta.
/// </summary>
public static partial class Money
{
    public const string InvalidMessage = "Escribe el monto solo con números, por ejemplo 1250.00.";

    public static string Format(decimal amount) =>
        (amount < 0 ? "−Q " : "Q ") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Valor para un campo de texto: sin separador de miles y con punto decimal.</summary>
    public static string? InputValue(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Interpreta "1250", "1250.5", "1,250.50", "1250,50" o "Q 1,250.00". El punto es el separador decimal; la coma
    /// separa miles, salvo que vaya seguida de uno o dos dígitos al final ("1250,50"). Un texto vacío es <c>null</c>.
    /// </summary>
    public static bool TryParse(string? text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var value = Whitespace().Replace(text, string.Empty);
        if (value.StartsWith('Q') || value.StartsWith('q'))
        {
            value = value[1..];
        }

        if (ThousandsWithDot().IsMatch(value))
        {
            value = value.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (CommaDecimal().IsMatch(value))
        {
            value = value.Replace(',', '.');
        }

        if (!Plain().IsMatch(value)
            || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        amount = parsed;
        return true;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+(\.\d+)?$")]
    private static partial Regex ThousandsWithDot();

    [GeneratedRegex(@"^-?\d+,\d{1,2}$")]
    private static partial Regex CommaDecimal();

    [GeneratedRegex(@"^-?\d{1,12}(\.\d{1,6})?$")]
    private static partial Regex Plain();
}
