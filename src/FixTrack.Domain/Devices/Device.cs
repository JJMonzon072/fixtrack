using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;

namespace FixTrack.Domain.Devices;

/// <summary>Equipo de un cliente. Puede tener varias órdenes de servicio a lo largo del tiempo.</summary>
public sealed class Device : Entity
{
    public const int BrandMaxLength = 60;
    public const int ModelMaxLength = 100;
    public const int SerialNumberMaxLength = 100;
    public const int NotesMaxLength = 500;

    public Device(int customerId, DeviceType type, string brand, string? model, string? serialNumber, string? notes, DateTime createdAtUtc)
    {
        if (customerId <= 0)
        {
            throw new DomainException("El equipo debe pertenecer a un cliente.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("El tipo de equipo no es válido.");
        }

        CustomerId = customerId;
        Type = type;
        Brand = Required(brand, BrandMaxLength, "La marca");
        Model = Optional(model, ModelMaxLength, "El modelo");
        SerialNumber = Optional(serialNumber, SerialNumberMaxLength, "El número de serie");
        Notes = Optional(notes, NotesMaxLength, "Las notas");
        CreatedAtUtc = UtcClock.Ensure(createdAtUtc, nameof(createdAtUtc));
    }

    // Constructor para Entity Framework Core.
    private Device()
    {
    }

    public int CustomerId { get; private set; }

    public Customer? Customer { get; private set; }

    public DeviceType Type { get; private set; }

    public string Brand { get; private set; } = string.Empty;

    public string? Model { get; private set; }

    /// <summary>Número de serie del fabricante. Es único por marca cuando se registra.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Estado físico, accesorios entregados u otras observaciones.</summary>
    public string? Notes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    private static string Required(string? value, int maxLength, string field)
    {
        var cleaned = Text.Clean(value);
        if (cleaned.Length == 0)
        {
            throw new DomainException($"{field} es obligatoria.");
        }

        return cleaned.Length <= maxLength
            ? cleaned
            : throw new DomainException($"{field} no puede superar {maxLength} caracteres.");
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        var cleaned = Text.CleanOrNull(value);
        return cleaned is null || cleaned.Length <= maxLength
            ? cleaned
            : throw new DomainException($"{field} no puede superar {maxLength} caracteres.");
    }
}
