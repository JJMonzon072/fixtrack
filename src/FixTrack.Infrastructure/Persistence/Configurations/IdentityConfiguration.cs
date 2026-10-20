using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class FixTrackUserConfiguration : IEntityTypeConfiguration<FixTrackUser>
{
    public void Configure(EntityTypeBuilder<FixTrackUser> builder)
    {
        builder.Property(user => user.FullName).HasMaxLength(FixTrackUser.FullNameMaxLength);
        builder.ToTable(table => table.HasCheckConstraint("CK_AspNetUsers_FullName_NotEmpty", "[FullName] <> ''"));
    }
}

/// <summary>
/// Los dos roles existen desde la migración: no hace falta crearlos al iniciar la aplicación.
/// Sus identificadores son fijos para que la migración sea reproducible.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole>
{
    public void Configure(EntityTypeBuilder<IdentityRole> builder) =>
        builder.HasData(
            new IdentityRole
            {
                Id = "6f1c2a8e-3b4d-4c5e-9f60-7a8b9c0d1e21",
                Name = Roles.Administrator,
                NormalizedName = Roles.Administrator.ToUpperInvariant(),
                ConcurrencyStamp = "0b7d6c5e-4f3a-4b2c-8d1e-9a0b1c2d3e41",
            },
            new IdentityRole
            {
                Id = "8a2d3b9f-4c5e-4d6f-a071-8b9c0d1e2f32",
                Name = Roles.Operator,
                NormalizedName = Roles.Operator.ToUpperInvariant(),
                ConcurrencyStamp = "1c8e7d6f-5a4b-4c3d-9e2f-0a1b2c3d4e52",
            });
}
