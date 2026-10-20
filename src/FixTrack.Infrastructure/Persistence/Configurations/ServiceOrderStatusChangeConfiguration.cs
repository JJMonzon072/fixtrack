using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class ServiceOrderStatusChangeConfiguration : IEntityTypeConfiguration<ServiceOrderStatusChange>
{
    /// <summary>Trigger que impide modificar o eliminar el historial (se crea en la migración inicial).</summary>
    public const string AppendOnlyTrigger = "TR_ServiceOrderStatusHistory_AppendOnly";

    public void Configure(EntityTypeBuilder<ServiceOrderStatusChange> builder)
    {
        builder.ToTable("ServiceOrderStatusHistory", table =>
        {
            table.HasCheckConstraint("CK_ServiceOrderStatusHistory_ToStatus", "[ToStatus] BETWEEN 1 AND 7");
            table.HasCheckConstraint(
                "CK_ServiceOrderStatusHistory_FromStatus",
                "[FromStatus] IS NULL OR ([FromStatus] BETWEEN 1 AND 7 AND [FromStatus] <> [ToStatus])");

            // Informa a EF Core que la tabla tiene un trigger para que genere SQL compatible al insertar.
            table.HasTrigger(AppendOnlyTrigger);
        });

        builder.HasKey(change => change.Id);
        builder.Property(change => change.Note).HasMaxLength(ServiceOrderStatusChange.NoteMaxLength);

        builder.HasIndex(change => new { change.ServiceOrderId, change.ChangedAtUtc })
            .HasDatabaseName("IX_ServiceOrderStatusHistory_ServiceOrderId_ChangedAtUtc");
    }
}
