using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;

namespace FixTrack.Domain.Devices;

/// <summary>
/// Equipo de un cliente. Puede tener varias órdenes de servicio a lo largo del tiempo.
/// Se registra con <see cref="Customer.RegisterDevice"/>; su propietario no cambia,
/// porque el historial de reparaciones queda vinculado al equipo.
/// </summary>
public sealed class Device : Entity
{
    internal Device(Customer owner, DeviceType type, string brand, string? model, string? serialNumber, string? notes, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(owner);

        SetDetails(type, brand, model, serialNumber, notes);
        Customer = owner;
        CustomerId = owner.Id;
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

    /// <summary>Número de serie del fabricante, en mayúsculas. Es único por marca cuando se registra.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Estado físico, accesorios entregados u otras observaciones.</summary>
    public string? Notes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Actualiza los datos técnicos y las observaciones. El propietario no se modifica.</summary>
    public void UpdateDetails(DeviceType type, string brand, string? model, string? serialNumber, string? notes, DateTime updatedAtUtc)
    {
        SetDetails(type, brand, model, serialNumber, notes);
        UpdatedAtUtc = UtcClock.Ensure(updatedAtUtc, nameof(updatedAtUtc));
    }

    private void SetDetails(DeviceType type, string brand, string? model, string? serialNumber, string? notes)
    {
        var cleanBrand = Text.Clean(brand);
        var cleanModel = Text.CleanOrNull(model);
        var cleanSerial = DeviceRules.NormalizeSerialNumber(serialNumber);
        var cleanNotes = Text.CleanMultilineOrNull(notes);

        var errors = DeviceRules.Validate(type, cleanBrand, cleanModel, cleanSerial, cleanNotes);
        if (errors.Count > 0)
        {
            throw new DomainException(errors.Values.First());
        }

        Type = type;
        Brand = cleanBrand;
        Model = cleanModel;
        SerialNumber = cleanSerial;
        Notes = cleanNotes;
    }
}
