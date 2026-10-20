using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", table =>
        {
            table.HasCheckConstraint("CK_Customers_FullName_NotEmpty", "[FullName] <> ''");
            table.HasCheckConstraint("CK_Customers_Phone_NotEmpty", "[Phone] <> ''");
            table.HasCheckConstraint("CK_Customers_Email_Format", "[Email] IS NULL OR [Email] LIKE '%_@_%._%'");
            table.HasCheckConstraint(
                "CK_Customers_Deactivation",
                "([IsActive] = 1 AND [DeactivatedAtUtc] IS NULL) OR ([IsActive] = 0 AND [DeactivatedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(customer => customer.Id);

        // Concurrencia optimista: el UPDATE incluye "WHERE Version = <versión leída>" (ver ServiceOrderConfiguration).
        builder.Property(customer => customer.Version).IsConcurrencyToken();

        builder.Property(customer => customer.FullName)
            .HasMaxLength(ContactRules.NameMaxLength)
            .UseCollation(Collations.SpanishAccentInsensitive);

        builder.Property(customer => customer.Phone)
            .HasMaxLength(ContactRules.PhoneMaxLength)
            .IsUnicode(false);

        builder.Property(customer => customer.Email).HasMaxLength(ContactRules.EmailMaxLength);
        builder.Property(customer => customer.Notes).HasMaxLength(CustomerRules.NotesMaxLength);

        builder.HasIndex(customer => customer.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("UX_Customers_Email");

        builder.HasIndex(customer => new { customer.IsActive, customer.FullName })
            .HasDatabaseName("IX_Customers_IsActive_FullName");

        builder.HasIndex(customer => customer.Phone).HasDatabaseName("IX_Customers_Phone");

        // Un cliente con equipos no puede eliminarse: se desactiva.
        builder.HasMany(customer => customer.Devices)
            .WithOne(device => device.Customer)
            .HasForeignKey(device => device.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
