using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class UserProfileMap : IEntityTypeConfiguration<UserProfile>
{
    public const string ExternalSubjectIndex = "ux_user_external_subject";

    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user", table => table.HasCheckConstraint(
            "ck_user_deletion_state",
            "(is_deleted AND deletion_cascade_id IS NOT NULL) OR (NOT is_deleted AND deletion_cascade_id IS NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicId).IsRequired();
        builder.Property(x => x.ExternalSubject).HasColumnType("text");
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayCurrencyId).IsRequired();
        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DeletionCascadeId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.ExternalSubject)
            .HasDatabaseName(ExternalSubjectIndex)
            .IsUnique();
        builder.HasOne(x => x.DisplayCurrency)
            .WithMany()
            .HasForeignKey(x => x.DisplayCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
