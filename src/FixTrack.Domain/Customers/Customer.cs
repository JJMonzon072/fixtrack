using FixTrack.Domain.Common;
using FixTrack.Domain.Devices;

namespace FixTrack.Domain.Customers;

/// <summary>
/// Cliente del taller. No se elimina: se desactiva para conservar su historial de equipos y órdenes.
/// </summary>
public sealed class Customer : Entity
{
    private readonly List<Device> _devices = [];

    public Customer(string fullName, string phone, string? email, string? notes, DateTime createdAtUtc)
    {
        SetDetails(fullName, phone, email, notes);
        IsActive = true;
        CreatedAtUtc = UtcClock.Ensure(createdAtUtc, nameof(createdAtUtc));
    }

    // Constructor para Entity Framework Core.
    private Customer()
    {
    }

    /// <summary>Nombre completo de la persona o razón social de la empresa.</summary>
    public string FullName { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    /// <summary>Correo electrónico en minúsculas. Es único entre los clientes cuando existe.</summary>
    public string? Email { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Aumenta con cada cambio. Es el token de concurrencia optimista: el formulario de edición envía la versión que vio
    /// y, si otra persona guardó antes, el cambio se rechaza en lugar de sobrescribir el suyo.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Momento de la última desactivación. Es <c>null</c> mientras el cliente está activo.</summary>
    public DateTime? DeactivatedAtUtc { get; private set; }

    public IReadOnlyCollection<Device> Devices => _devices;

    /// <summary>
    /// Registra un equipo a nombre de este cliente. Solo los clientes activos admiten equipos nuevos;
    /// los equipos de un cliente inactivo se conservan y siguen siendo consultables.
    /// </summary>
    public Device RegisterDevice(
        DeviceType type,
        string brand,
        string? model,
        string? serialNumber,
        string? notes,
        DateTime registeredAtUtc)
    {
        if (!IsActive)
        {
            throw new DomainException("No se pueden registrar equipos para un cliente inactivo.");
        }

        var device = new Device(this, type, brand, model, serialNumber, notes, registeredAtUtc);
        _devices.Add(device);
        return device;
    }

    public void UpdateDetails(string fullName, string phone, string? email, string? notes, DateTime updatedAtUtc)
    {
        SetDetails(fullName, phone, email, notes);
        UpdatedAtUtc = UtcClock.Ensure(updatedAtUtc, nameof(updatedAtUtc));
        Version++;
    }

    public void Deactivate(DateTime deactivatedAtUtc)
    {
        if (!IsActive)
        {
            throw new DomainException("El cliente ya está inactivo.");
        }

        IsActive = false;
        DeactivatedAtUtc = UtcClock.Ensure(deactivatedAtUtc, nameof(deactivatedAtUtc));
        UpdatedAtUtc = DeactivatedAtUtc;
        Version++;
    }

    public void Reactivate(DateTime reactivatedAtUtc)
    {
        if (IsActive)
        {
            throw new DomainException("El cliente ya está activo.");
        }

        IsActive = true;
        DeactivatedAtUtc = null;
        UpdatedAtUtc = UtcClock.Ensure(reactivatedAtUtc, nameof(reactivatedAtUtc));
        Version++;
    }

    private void SetDetails(string fullName, string phone, string? email, string? notes)
    {
        var cleanName = Text.Clean(fullName);
        var cleanPhone = Text.Clean(phone);
        var cleanEmail = ContactRules.NormalizeEmail(email);
        var cleanNotes = Text.CleanMultilineOrNull(notes);

        var errors = CustomerRules.Validate(cleanName, cleanPhone, cleanEmail, cleanNotes);
        if (errors.Count > 0)
        {
            throw new DomainException(errors.Values.First());
        }

        FullName = cleanName;
        Phone = cleanPhone;
        Email = cleanEmail;
        Notes = cleanNotes;
    }
}
