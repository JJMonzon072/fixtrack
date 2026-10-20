using FixTrack.Domain.Common;

namespace FixTrack.Domain.Technicians;

/// <summary>
/// Técnico del taller. Se desactiva en lugar de eliminarse para conservar sus órdenes.
/// Un técnico inactivo no puede recibir nuevas asignaciones.
/// </summary>
public sealed class Technician : Entity
{
    public Technician(string fullName, string specialty, string? phone, string? email, DateTime createdAtUtc)
    {
        SetDetails(fullName, specialty, phone, email);
        IsActive = true;
        CreatedAtUtc = UtcClock.Ensure(createdAtUtc, nameof(createdAtUtc));
    }

    // Constructor para Entity Framework Core.
    private Technician()
    {
    }

    public string FullName { get; private set; } = string.Empty;

    /// <summary>Área de trabajo, por ejemplo "Laptops y equipos de escritorio".</summary>
    public string Specialty { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    /// <summary>Correo electrónico en minúsculas. Es único entre los técnicos cuando existe.</summary>
    public string? Email { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Momento de la última desactivación. Es <c>null</c> mientras el técnico está activo.</summary>
    public DateTime? DeactivatedAtUtc { get; private set; }

    public void UpdateDetails(string fullName, string specialty, string? phone, string? email, DateTime updatedAtUtc)
    {
        SetDetails(fullName, specialty, phone, email);
        UpdatedAtUtc = UtcClock.Ensure(updatedAtUtc, nameof(updatedAtUtc));
    }

    /// <summary>
    /// Desactiva al técnico. La capa de aplicación verifica antes que no tenga órdenes abiertas asignadas.
    /// </summary>
    public void Deactivate(DateTime deactivatedAtUtc)
    {
        if (!IsActive)
        {
            throw new DomainException("El técnico ya está inactivo.");
        }

        IsActive = false;
        DeactivatedAtUtc = UtcClock.Ensure(deactivatedAtUtc, nameof(deactivatedAtUtc));
        UpdatedAtUtc = DeactivatedAtUtc;
    }

    public void Reactivate(DateTime reactivatedAtUtc)
    {
        if (IsActive)
        {
            throw new DomainException("El técnico ya está activo.");
        }

        IsActive = true;
        DeactivatedAtUtc = null;
        UpdatedAtUtc = UtcClock.Ensure(reactivatedAtUtc, nameof(reactivatedAtUtc));
    }

    private void SetDetails(string fullName, string specialty, string? phone, string? email)
    {
        var cleanName = Text.Clean(fullName);
        var cleanSpecialty = Text.Clean(specialty);
        var cleanPhone = Text.CleanOrNull(phone);
        var cleanEmail = ContactRules.NormalizeEmail(email);

        var errors = TechnicianRules.Validate(cleanName, cleanSpecialty, cleanPhone, cleanEmail);
        if (errors.Count > 0)
        {
            throw new DomainException(errors.Values.First());
        }

        FullName = cleanName;
        Specialty = cleanSpecialty;
        Phone = cleanPhone;
        Email = cleanEmail;
    }
}
