using ArturRios.Fortuna.Domain.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class CategoryMap : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("category", table => table.HasCheckConstraint(
            "ck_category_deletion_state",
            "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
            "(NOT is_deleted AND deletion_cascade_id IS NULL)"));
        builder.HasKey(category => category.Id);
        builder.Property(category => category.PublicId).IsRequired();
        builder.Property(category => category.UserId).IsRequired();
        builder.Property(category => category.ParentId);
        builder.Property(category => category.Name).HasMaxLength(200).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(category => category.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(category => category.DeletionCascadeId);
        builder.Property(category => category.CreatedAt).IsRequired();
        builder.Property(category => category.UpdatedAt).IsRequired();
        builder.HasIndex(category => category.PublicId).IsUnique();
        builder.HasIndex(category => new { category.UserId, category.NormalizedName })
            .IsUnique()
            .HasFilter("parent_id IS NULL AND NOT is_deleted");
        builder.HasIndex(category => new
        {
            category.UserId,
            category.ParentId,
            category.NormalizedName
        })
            .IsUnique()
            .HasFilter("parent_id IS NOT NULL AND NOT is_deleted");
        builder.HasIndex(category => new { category.UserId, category.IsDeleted });
        builder.HasOne(category => category.User)
            .WithMany()
            .HasForeignKey(category => category.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(category => category.Parent)
            .WithMany()
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
