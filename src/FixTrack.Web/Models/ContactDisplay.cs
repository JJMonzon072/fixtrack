namespace FixTrack.Web.Models;

/// <summary>Presentación de datos de contacto de personas (clientes y técnicos).</summary>
public static class ContactDisplay
{
    /// <summary>Iniciales para el avatar: primera letra de las dos primeras palabras.</summary>
    public static string Initials(string fullName)
    {
        var letters = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.FirstOrDefault(char.IsLetterOrDigit))
            .Where(letter => letter != default)
            .Take(2)
            .Select(char.ToUpperInvariant);
        return string.Concat(letters);
    }

    /// <summary>Número para enlaces tel: (solo dígitos y '+').</summary>
    public static string PhoneHref(string phone) =>
        "tel:" + string.Concat(phone.Where(c => char.IsAsciiDigit(c) || c == '+'));
}
