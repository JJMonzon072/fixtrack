using FixTrack.Domain.Common;

namespace FixTrack.Domain.Technicians;

/// <summary>Técnico del taller. Se desactiva en lugar de eliminarse para conservar sus órdenes.</summary>
public sealed class Technician : Entity
{
    public const int SpecialtyMaxLength = 100;

    public Technician(string fullName, string? phone, string? email, string? specialty, DateTime createdAtUtc)
    {
        var cleanName = Text.Clean(fullName);
        var cleanPhone = Text.CleanOrNull(phone);
        var cleanEmail = ContactRules.NormalizeEmail(email);
        var cleanSpecialty = Text.CleanOrNull(specialty);

        var error = ContactRules.ValidateName(cleanName)
            ?? (cleanPhone is null ? null : ContactRules.ValidatePhone(cleanPhone))
            ?? (cleanEmail is null ? null : ContactRules.ValidateEmail(cleanEmail))
            ?? (cleanSpecialty is { Length: > SpecialtyMaxLength }
                ? $"La especialidad no puede superar {SpecialtyMaxLength} caracteres."
                : null);

        if (error is not null)
        {
            throw new DomainException(error);
        }

        FullName = cleanName;
        Phone = cleanPhone;
        Email = cleanEmail;
        Specialty = cleanSpecialty;
        IsActive = true;
        CreatedAtUtc = UtcClock.Ensure(createdAtUtc, nameof(createdAtUtc));
    }

    // Constructor para Entity Framework Core.
    private Technician()
    {
    }

    public string FullName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    /// <summary>Correo electrónico en minúsculas. Es único entre los técnicos cuando existe.</summary>
    public string? Email { get; private set; }

    public string? Specialty { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public DateTime? DeactivatedAtUtc { get; private set; }
}
