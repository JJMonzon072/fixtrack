using System.Text.RegularExpressions;
using FixTrack.Domain.Common;

namespace FixTrack.Domain.Devices;

/// <summary>Reglas de validación y normalización de los datos de un equipo.</summary>
public static partial class DeviceRules
{
    public const int BrandMaxLength = 60;
    public const int ModelMaxLength = 100;
    public const int SerialNumberMaxLength = 100;
    public const int NotesMaxLength = 500;

    /// <summary>Los números de serie se guardan en mayúsculas y sin espacios sobrantes.</summary>
    public static string? NormalizeSerialNumber(string? serialNumber) =>
        Text.CleanOrNull(serialNumber)?.ToUpperInvariant();

    /// <summary>
    /// Valida datos ya normalizados y devuelve los errores por campo
    /// (claves: nombres de las propiedades de <see cref="Device"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Validate(
        DeviceType? type,
        string brand,
        string? model,
        string? serialNumber,
        string? notes)
    {
        var errors = new Dictionary<string, string>();

        if (type is null || !Enum.IsDefined(type.Value))
        {
            errors[nameof(Device.Type)] = "Selecciona el tipo de equipo.";
        }

        if (brand.Length == 0)
        {
            errors[nameof(Device.Brand)] = "La marca es obligatoria.";
        }
        else if (brand.Length > BrandMaxLength)
        {
            errors[nameof(Device.Brand)] = $"La marca no puede superar {BrandMaxLength} caracteres.";
        }
        else if (!brand.Any(char.IsLetterOrDigit))
        {
            errors[nameof(Device.Brand)] = "La marca debe contener letras o números.";
        }

        if (model is { Length: > ModelMaxLength })
        {
            errors[nameof(Device.Model)] = $"El modelo no puede superar {ModelMaxLength} caracteres.";
        }

        if (serialNumber is not null)
        {
            if (serialNumber.Length > SerialNumberMaxLength)
            {
                errors[nameof(Device.SerialNumber)] = $"El número de serie no puede superar {SerialNumberMaxLength} caracteres.";
            }
            else if (!SerialNumberPattern().IsMatch(serialNumber))
            {
                errors[nameof(Device.SerialNumber)] =
                    "El número de serie solo admite letras sin tilde, números, espacios, guiones, puntos y barras.";
            }
        }

        if (notes is { Length: > NotesMaxLength })
        {
            errors[nameof(Device.Notes)] = $"Las observaciones no pueden superar {NotesMaxLength} caracteres.";
        }

        return errors;
    }

    // La columna es varchar: solo se admiten caracteres ASCII imprimibles habituales en series.
    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9 ._/\-]*$")]
    private static partial Regex SerialNumberPattern();
}
