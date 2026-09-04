using ArturRios.Fortuna.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class AuditEntryMap : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entry");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Operation).HasMaxLength(150).IsRequired();
        builder.Property(entry => entry.EntityType).HasMaxLength(100);
        builder.Property(entry => entry.Reason).HasMaxLength(1000);
        builder.Property(entry => entry.Outcome).IsRequired();
        builder.Property(entry => entry.OccurredAt).IsRequired();
        builder.Property(entry => entry.ActorUserId);
        builder.HasIndex(entry => entry.ActorUserId);
        builder.HasIndex(entry => entry.OccurredAt);
    }
}
