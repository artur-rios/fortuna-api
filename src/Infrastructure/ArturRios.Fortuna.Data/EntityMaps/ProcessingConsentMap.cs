using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class ProcessingConsentMap : IEntityTypeConfiguration<ProcessingConsent>
{
    public void Configure(EntityTypeBuilder<ProcessingConsent> builder)
    {
        builder.ToTable("processing_consent", table => table.HasCheckConstraint(
            "ck_processing_consent_purpose", "purpose = 1"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.PublicId).IsRequired();
        builder.Property(item => item.UserId).IsRequired();
        builder.Property(item => item.Purpose).IsRequired();
        builder.Property(item => item.Version).HasMaxLength(50).IsRequired();
        builder.Property(item => item.GrantedAt).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.UserId, item.Purpose }).IsUnique();
        builder.HasOne(item => item.User)
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
