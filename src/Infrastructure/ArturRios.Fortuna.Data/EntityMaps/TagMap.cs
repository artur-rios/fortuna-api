using ArturRios.Fortuna.Domain.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class TagMap : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tag", table => table.HasCheckConstraint(
            "ck_tag_deletion_state",
            "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
            "(NOT is_deleted AND deletion_cascade_id IS NULL)"));
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.PublicId).IsRequired();
        builder.Property(tag => tag.UserId).IsRequired();
        builder.Property(tag => tag.Name).HasMaxLength(200).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(tag => tag.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(tag => tag.DeletionCascadeId);
        builder.Property(tag => tag.CreatedAt).IsRequired();
        builder.Property(tag => tag.UpdatedAt).IsRequired();
        builder.HasIndex(tag => tag.PublicId).IsUnique();
        builder.HasIndex(tag => new { tag.UserId, tag.NormalizedName })
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(tag => new { tag.UserId, tag.IsDeleted });
        builder.HasOne(tag => tag.User)
            .WithMany()
            .HasForeignKey(tag => tag.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
