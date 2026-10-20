using FixTrack.Domain.Common;
using FixTrack.Domain.Technicians;

namespace FixTrack.Tests.Domain;

public sealed class TechnicianTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_technician_is_active_and_its_data_is_normalized()
    {
        var technician = new Technician("  Luis   Pérez ", " Laptops  y redes ", "  ", " Luis@Example.com ", Now);

        Assert.Equal("Luis Pérez", technician.FullName);
        Assert.Equal("Laptops y redes", technician.Specialty);
        Assert.Null(technician.Phone);
        Assert.Equal("luis@example.com", technician.Email);
        Assert.True(technician.IsActive);
        Assert.Equal(Now, technician.CreatedAtUtc);
    }

    [Fact]
    public void The_specialty_is_required_and_limited()
    {
        Assert.Contains("especialidad es obligatoria", Assert.Throws<DomainException>(() =>
            new Technician("Luis Pérez", " ", null, null, Now)).Message);

        var errors = TechnicianRules.Validate("Luis Pérez", new string('a', TechnicianRules.SpecialtyMaxLength + 1), null, null);
        Assert.True(errors.ContainsKey(nameof(Technician.Specialty)));
    }

    [Fact]
    public void Optional_contact_data_must_be_valid_when_provided()
    {
        var errors = TechnicianRules.Validate("Luis Pérez", "Impresoras", "123", "no-es-correo");

        Assert.Equal([nameof(Technician.Email), nameof(Technician.Phone)], errors.Keys.Order());
        Assert.Empty(TechnicianRules.Validate("Luis Pérez", "Impresoras", null, null));
    }

    [Fact]
    public void Updating_details_records_the_update_time()
    {
        var technician = new Technician("Luis Pérez", "Laptops", null, null, Now);

        technician.UpdateDetails("Luis Alberto Pérez", "Laptops y equipos de escritorio", "5555-0101", "luis@example.com", Now.AddHours(1));

        Assert.Equal("Luis Alberto Pérez", technician.FullName);
        Assert.Equal("Laptops y equipos de escritorio", technician.Specialty);
        Assert.Equal("5555-0101", technician.Phone);
        Assert.Equal(Now.AddHours(1), technician.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivating_and_reactivating_keeps_the_record()
    {
        var technician = new Technician("Luis Pérez", "Laptops", null, null, Now);

        technician.Deactivate(Now.AddDays(1));
        Assert.False(technician.IsActive);
        Assert.Equal(Now.AddDays(1), technician.DeactivatedAtUtc);
        Assert.Throws<DomainException>(() => technician.Deactivate(Now.AddDays(2)));

        technician.Reactivate(Now.AddDays(3));
        Assert.True(technician.IsActive);
        Assert.Null(technician.DeactivatedAtUtc);
        Assert.Equal(Now.AddDays(3), technician.UpdatedAtUtc);
        Assert.Throws<DomainException>(() => technician.Reactivate(Now.AddDays(4)));
    }
}
