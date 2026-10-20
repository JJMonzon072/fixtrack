using FixTrack.Domain.Common;

namespace FixTrack.Domain.Technicians;

/// <summary>Reglas de validación de los datos de un técnico.</summary>
public static class TechnicianRules
{
    public const int SpecialtyMinLength = 3;
    public const int SpecialtyMaxLength = 100;

    /// <summary>
    /// Valida datos ya normalizados y devuelve los errores por campo
    /// (claves: nombres de las propiedades de <see cref="Technician"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(string fullName, string specialty, string? phone, string? email)
    {
        var errors = new Dictionary<string, string>();

        if (ContactRules.ValidateName(fullName) is { } nameError)
        {
            errors[nameof(Technician.FullName)] = nameError;
        }

        if (specialty.Length == 0)
        {
            errors[nameof(Technician.Specialty)] = "La especialidad es obligatoria.";
        }
        else if (specialty.Length is < SpecialtyMinLength or > SpecialtyMaxLength)
        {
            errors[nameof(Technician.Specialty)] =
                $"La especialidad debe tener entre {SpecialtyMinLength} y {SpecialtyMaxLength} caracteres.";
        }

        if (phone is not null && ContactRules.ValidatePhone(phone) is { } phoneError)
        {
            errors[nameof(Technician.Phone)] = phoneError;
        }

        if (email is not null && ContactRules.ValidateEmail(email) is { } emailError)
        {
            errors[nameof(Technician.Email)] = emailError;
        }

        return errors;
    }
}
