using System.Net.Mail;

namespace FixTrack.Domain.Common;

/// <summary>
/// Reglas de formato para datos de contacto de personas y empresas.
/// Cada método devuelve el mensaje de error o <c>null</c> si el valor es válido.
/// </summary>
public static class ContactRules
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 150;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 20;
    public const int PhoneMinDigits = 7;
    public const int PhoneMaxDigits = 15;

    private const string NameSymbols = " .,'’-&()";
    private const string PhoneSymbols = " -().";

    /// <summary>Nombre de una persona o razón social: letras, números y puntuación habitual.</summary>
    public static string? ValidateName(string name)
    {
        if (name.Length == 0)
        {
            return "El nombre es obligatorio.";
        }

        if (name.Length is < NameMinLength or > NameMaxLength)
        {
            return $"El nombre debe tener entre {NameMinLength} y {NameMaxLength} caracteres.";
        }

        if (name.Count(char.IsLetter) < 2)
        {
            return "El nombre debe contener al menos dos letras.";
        }

        return name.All(c => char.IsLetterOrDigit(c) || IsCombiningMark(c) || NameSymbols.Contains(c))
            ? null
            : "El nombre contiene caracteres no permitidos.";
    }

    /// <summary>Teléfono local o internacional: de 7 a 15 dígitos, con '+' opcional al inicio.</summary>
    public static string? ValidatePhone(string phone)
    {
        if (phone.Length == 0)
        {
            return "El teléfono es obligatorio.";
        }

        if (phone.Length > PhoneMaxLength)
        {
            return $"El teléfono no puede superar {PhoneMaxLength} caracteres.";
        }

        var body = phone.StartsWith('+') ? phone[1..] : phone;
        if (!body.All(c => char.IsAsciiDigit(c) || PhoneSymbols.Contains(c)))
        {
            return "El teléfono solo puede contener dígitos, espacios, guiones, paréntesis y un '+' inicial.";
        }

        var digits = body.Count(char.IsAsciiDigit);
        return digits is >= PhoneMinDigits and <= PhoneMaxDigits
            ? null
            : $"El teléfono debe tener entre {PhoneMinDigits} y {PhoneMaxDigits} dígitos.";
    }

    /// <summary>Correo electrónico con formato usuario@dominio.ext.</summary>
    public static string? ValidateEmail(string email)
    {
        if (email.Length > EmailMaxLength)
        {
            return $"El correo electrónico no puede superar {EmailMaxLength} caracteres.";
        }

        var at = email.IndexOf('@');
        var isValid = at > 0
            && at == email.LastIndexOf('@')
            && email.IndexOf('.', at) > at + 1
            && !email.EndsWith('.')
            && !email.Any(char.IsWhiteSpace)
            && MailAddress.TryCreate(email, out var address)
            && address.Address == email;

        return isValid ? null : "Ingresa un correo electrónico válido, por ejemplo nombre@dominio.com.";
    }

    /// <summary>Normaliza un correo electrónico: sin espacios y en minúsculas.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var cleaned = Text.CleanOrNull(email);
        return cleaned?.ToLowerInvariant();
    }

    private static bool IsCombiningMark(char c) =>
        char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;
}
