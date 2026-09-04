using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class TransferMap : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfer", table =>
        {
            table.HasCheckConstraint(
                "ck_transfer_movements",
                "outbound_transaction_id <> inbound_transaction_id");
            table.HasCheckConstraint(
                "ck_transfer_conversion",
                "(applied_rate IS NULL AND rate_date IS NULL) OR " +
                "(applied_rate > 0 AND rate_date IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transfer_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(transfer => transfer.Id);
        builder.Property(transfer => transfer.PublicId).IsRequired();
        builder.Property(transfer => transfer.OutboundTransactionId).IsRequired();
        builder.Property(transfer => transfer.InboundTransactionId).IsRequired();
        builder.Property(transfer => transfer.AppliedRate).HasPrecision(19, 8);
        builder.Property(transfer => transfer.RateDate);
        builder.Property(transfer => transfer.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(transfer => transfer.DeletionCascadeId);
        builder.Property(transfer => transfer.CreatedAt).IsRequired();
        builder.Property(transfer => transfer.UpdatedAt).IsRequired();
        builder.HasIndex(transfer => transfer.PublicId).IsUnique();
        builder.HasIndex(transfer => transfer.OutboundTransactionId).IsUnique();
        builder.HasIndex(transfer => transfer.InboundTransactionId).IsUnique();
        builder.HasOne(transfer => transfer.OutboundTransaction)
            .WithMany()
            .HasForeignKey(transfer => transfer.OutboundTransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(transfer => transfer.InboundTransaction)
            .WithMany()
            .HasForeignKey(transfer => transfer.InboundTransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
