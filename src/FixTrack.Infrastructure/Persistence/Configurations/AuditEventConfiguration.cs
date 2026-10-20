using FixTrack.Domain.Auditing;
using FixTrack.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixTrack.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    /// <summary>Trigger que impide modificar o eliminar eventos de auditoría (se crea en su migración).</summary>
    public const string AppendOnlyTrigger = "TR_AuditEvents_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents", table =>
        {
            table.HasCheckConstraint("CK_AuditEvents_Summary_NotEmpty", "[Summary] <> ''");
            table.HasTrigger(AppendOnlyTrigger);
        });

        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.UserId).HasMaxLength(Actor.UserIdMaxLength);
        builder.Property(audit => audit.UserName).HasMaxLength(Actor.NameMaxLength);
        builder.Property(audit => audit.Action).HasMaxLength(AuditEvent.ActionMaxLength).IsUnicode(false);
        builder.Property(audit => audit.EntityType).HasMaxLength(AuditEvent.EntityTypeMaxLength).IsUnicode(false);
        builder.Property(audit => audit.EntityId).HasMaxLength(AuditEvent.EntityIdMaxLength).IsUnicode(false);
        builder.Property(audit => audit.Summary).HasMaxLength(AuditEvent.SummaryMaxLength);

        // Listado general (más recientes primero) y actividad de un registro concreto.
        builder.HasIndex(audit => audit.OccurredAtUtc).HasDatabaseName("IX_AuditEvents_OccurredAtUtc");
        builder.HasIndex(audit => new { audit.EntityType, audit.EntityId, audit.OccurredAtUtc })
            .HasDatabaseName("IX_AuditEvents_EntityType_EntityId_OccurredAtUtc");
    }
}
