using FixTrack.Domain.Common;
using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class ServiceOrderAssignmentConfiguration : IEntityTypeConfiguration<ServiceOrderAssignment>
{
    /// <summary>Trigger que impide modificar o eliminar las asignaciones (se crea en su migración).</summary>
    public const string AppendOnlyTrigger = "TR_ServiceOrderAssignments_AppendOnly";

    public void Configure(EntityTypeBuilder<ServiceOrderAssignment> builder)
    {
        builder.ToTable("ServiceOrderAssignments", table =>
        {
            table.HasCheckConstraint(
                "CK_ServiceOrderAssignments_DifferentTechnician",
                "[FromTechnicianId] IS NULL OR [FromTechnicianId] <> [ToTechnicianId]");

            // Informa a EF Core que la tabla tiene un trigger para que genere SQL compatible al insertar.
            table.HasTrigger(AppendOnlyTrigger);
        });

        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.AssignedByUserId).HasMaxLength(Actor.UserIdMaxLength);
        builder.Property(assignment => assignment.AssignedByName).HasMaxLength(Actor.NameMaxLength);
        builder.Property(assignment => assignment.Note).HasMaxLength(ServiceOrderAssignment.NoteMaxLength);

        builder.HasOne(assignment => assignment.ToTechnician)
            .WithMany()
            .HasForeignKey(assignment => assignment.ToTechnicianId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Technicians.Technician>()
            .WithMany()
            .HasForeignKey(assignment => assignment.FromTechnicianId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(assignment => new { assignment.ServiceOrderId, assignment.AssignedAtUtc })
            .HasDatabaseName("IX_ServiceOrderAssignments_ServiceOrderId_AssignedAtUtc");
    }
}
