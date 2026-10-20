using FixTrack.Domain.Common;
using FixTrack.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("Technicians", table =>
        {
            table.HasCheckConstraint("CK_Technicians_FullName_NotEmpty", "[FullName] <> ''");
            table.HasCheckConstraint("CK_Technicians_Email_Format", "[Email] IS NULL OR [Email] LIKE '%_@_%._%'");
            table.HasCheckConstraint(
                "CK_Technicians_Deactivation",
                "([IsActive] = 1 AND [DeactivatedAtUtc] IS NULL) OR ([IsActive] = 0 AND [DeactivatedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(technician => technician.Id);

        builder.Property(technician => technician.FullName)
            .HasMaxLength(ContactRules.NameMaxLength)
            .UseCollation(Collations.SpanishAccentInsensitive);

        builder.Property(technician => technician.Phone)
            .HasMaxLength(ContactRules.PhoneMaxLength)
            .IsUnicode(false);

        builder.Property(technician => technician.Email).HasMaxLength(ContactRules.EmailMaxLength);
        builder.Property(technician => technician.Specialty).HasMaxLength(Technician.SpecialtyMaxLength);

        builder.HasIndex(technician => technician.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("UX_Technicians_Email");

        builder.HasIndex(technician => new { technician.IsActive, technician.FullName })
            .HasDatabaseName("IX_Technicians_IsActive_FullName");
    }
}
