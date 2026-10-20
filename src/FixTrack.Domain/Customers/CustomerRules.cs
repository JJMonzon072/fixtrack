using FixTrack.Domain.Common;

namespace FixTrack.Domain.Customers;

/// <summary>Reglas de validación de los datos de un cliente.</summary>
public static class CustomerRules
{
    public const int NotesMaxLength = 500;

    /// <summary>
    /// Valida datos ya normalizados y devuelve los errores por campo
    /// (claves: nombres de las propiedades de <see cref="Customer"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(string fullName, string phone, string? email, string? notes)
    {
        var errors = new Dictionary<string, string>();

        Add(errors, nameof(Customer.FullName), ContactRules.ValidateName(fullName));
        Add(errors, nameof(Customer.Phone), ContactRules.ValidatePhone(phone));

        if (email is not null)
        {
            Add(errors, nameof(Customer.Email), ContactRules.ValidateEmail(email));
        }

        if (notes is { Length: > NotesMaxLength })
        {
            errors[nameof(Customer.Notes)] = $"Las notas no pueden superar {NotesMaxLength} caracteres.";
        }

        return errors;
    }

    private static void Add(Dictionary<string, string> errors, string field, string? error)
    {
        if (error is not null)
        {
            errors[field] = error;
        }
    }
}
