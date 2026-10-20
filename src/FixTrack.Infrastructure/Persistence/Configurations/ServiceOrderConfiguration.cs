using FixTrack.Domain.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class ServiceOrderConfiguration : IEntityTypeConfiguration<ServiceOrder>
{
    public void Configure(EntityTypeBuilder<ServiceOrder> builder)
    {
        builder.ToTable("ServiceOrders", table =>
        {
            table.HasCheckConstraint("CK_ServiceOrders_Status", "[Status] BETWEEN 1 AND 7");
            table.HasCheckConstraint("CK_ServiceOrders_Priority", "[Priority] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_ServiceOrders_ReportedIssue_NotEmpty", "[ReportedIssue] <> ''");
            table.HasCheckConstraint("CK_ServiceOrders_EstimatedCost", "[EstimatedCost] IS NULL OR [EstimatedCost] >= 0");
            table.HasCheckConstraint("CK_ServiceOrders_FinalCost", "[FinalCost] IS NULL OR [FinalCost] >= 0");
            table.HasCheckConstraint("CK_ServiceOrders_PromisedAt", "[PromisedAtUtc] IS NULL OR [PromisedAtUtc] >= [ReceivedAtUtc]");

            // Toda asignación de técnico registra su fecha.
            table.HasCheckConstraint(
                "CK_ServiceOrders_TechnicianAssignment",
                "([TechnicianId] IS NULL AND [TechnicianAssignedAtUtc] IS NULL) OR ([TechnicianId] IS NOT NULL AND [TechnicianAssignedAtUtc] IS NOT NULL)");

            // El costo final se registra al entregar (estado 6) y solo entonces.
            table.HasCheckConstraint(
                "CK_ServiceOrders_FinalCost_OnDelivery",
                "([Status] = 6 AND [FinalCost] IS NOT NULL) OR ([Status] <> 6 AND [FinalCost] IS NULL)");

            // La fecha de entrega existe solo cuando la orden está entregada (estado 6).
            table.HasCheckConstraint(
                "CK_ServiceOrders_Delivery",
                "([Status] = 6 AND [DeliveredAtUtc] IS NOT NULL) OR ([Status] <> 6 AND [DeliveredAtUtc] IS NULL)");
        });

        builder.HasKey(order => order.Id);
        builder.Ignore(order => order.Number);
        builder.Ignore(order => order.IsClosed);
        builder.Ignore(order => order.CostVariance);

        builder.Property(order => order.ReportedIssue).HasMaxLength(ServiceOrderRules.ReportedIssueMaxLength);
        builder.Property(order => order.IntakeNotes).HasMaxLength(ServiceOrderRules.IntakeNotesMaxLength);
        builder.Property(order => order.Diagnosis).HasMaxLength(ServiceOrderRules.DiagnosisMaxLength);

        // Concurrencia optimista: el UPDATE incluye "WHERE Version = <versión leída>". Si otra solicitud
        // modificó la orden mientras tanto, no se actualiza ninguna fila y EF Core lanza
        // DbUpdateConcurrencyException en lugar de sobrescribir el cambio ajeno.
        builder.Property(order => order.Version).IsConcurrencyToken();

        builder.HasOne(order => order.Device)
            .WithMany()
            .HasForeignKey(order => order.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(order => order.Technician)
            .WithMany()
            .HasForeignKey(order => order.TechnicianId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.StatusHistory)
            .WithOne()
            .HasForeignKey(change => change.ServiceOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.Assignments)
            .WithOne()
            .HasForeignKey(assignment => assignment.ServiceOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(order => order.DeviceId).HasDatabaseName("IX_ServiceOrders_DeviceId");
        builder.HasIndex(order => order.TechnicianId).HasDatabaseName("IX_ServiceOrders_TechnicianId");

        // Consulta principal de la priorización: órdenes por estado ordenadas por fecha comprometida.
        builder.HasIndex(order => new { order.Status, order.PromisedAtUtc })
            .HasDatabaseName("IX_ServiceOrders_Status_PromisedAtUtc");

        // Orden predeterminado del listado y de las órdenes recibidas recientemente.
        builder.HasIndex(order => order.ReceivedAtUtc).HasDatabaseName("IX_ServiceOrders_ReceivedAtUtc");
    }
}
