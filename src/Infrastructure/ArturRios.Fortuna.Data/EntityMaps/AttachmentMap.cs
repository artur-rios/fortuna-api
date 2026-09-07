using ArturRios.Fortuna.Domain.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class AttachmentMap : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachment", table => table.HasCheckConstraint(
            "ck_attachment_deletion_state",
            "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
            "(NOT is_deleted AND deletion_cascade_id IS NULL)"));
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.PublicId).IsRequired();
        builder.Property(attachment => attachment.TransactionId).IsRequired();
        builder.Property(attachment => attachment.FileName).HasMaxLength(300).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(attachment => attachment.SizeInBytes).IsRequired();
        builder.Property(attachment => attachment.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(attachment => attachment.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(attachment => attachment.DeletionCascadeId);
        builder.Property(attachment => attachment.CreatedAt).IsRequired();
        builder.Property(attachment => attachment.UpdatedAt).IsRequired();
        builder.HasIndex(attachment => attachment.PublicId).IsUnique();
        builder.HasIndex(attachment => new { attachment.TransactionId, attachment.IsDeleted });
        builder.HasOne(attachment => attachment.Transaction)
            .WithMany(transaction => transaction.Attachments)
            .HasForeignKey(attachment => attachment.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
