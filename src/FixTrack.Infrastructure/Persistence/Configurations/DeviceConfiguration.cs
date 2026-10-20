using FixTrack.Domain.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices", table =>
        {
            table.HasCheckConstraint("CK_Devices_Type", "[Type] BETWEEN 1 AND 7");
            table.HasCheckConstraint("CK_Devices_Brand_NotEmpty", "[Brand] <> ''");

            // Una serie vacía chocaría con otras en el índice único: se guarda NULL cuando no existe.
            table.HasCheckConstraint("CK_Devices_SerialNumber_NotEmpty", "[SerialNumber] IS NULL OR [SerialNumber] <> ''");
        });

        builder.HasKey(device => device.Id);

        // Concurrencia optimista: el UPDATE incluye "WHERE Version = <versión leída>" (ver ServiceOrderConfiguration).
        builder.Property(device => device.Version).IsConcurrencyToken();

        builder.Property(device => device.Brand).HasMaxLength(DeviceRules.BrandMaxLength);
        builder.Property(device => device.Model).HasMaxLength(DeviceRules.ModelMaxLength);
        builder.Property(device => device.SerialNumber).HasMaxLength(DeviceRules.SerialNumberMaxLength).IsUnicode(false);
        builder.Property(device => device.Notes).HasMaxLength(DeviceRules.NotesMaxLength);

        builder.HasIndex(device => device.CustomerId).HasDatabaseName("IX_Devices_CustomerId");

        // Un mismo número de serie no puede repetirse dentro de una marca.
        builder.HasIndex(device => new { device.Brand, device.SerialNumber })
            .IsUnique()
            .HasFilter("[SerialNumber] IS NOT NULL")
            .HasDatabaseName("UX_Devices_Brand_SerialNumber");
    }
}
