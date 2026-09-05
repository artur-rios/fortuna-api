using ArturRios.Fortuna.Domain.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class CounterpartyMap : IEntityTypeConfiguration<Counterparty>
{
    public void Configure(EntityTypeBuilder<Counterparty> builder)
    {
        builder.ToTable("counterparty", table => table.HasCheckConstraint(
            "ck_counterparty_deletion_state",
            "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
            "(NOT is_deleted AND deletion_cascade_id IS NULL)"));
        builder.HasKey(counterparty => counterparty.Id);
        builder.Property(counterparty => counterparty.PublicId).IsRequired();
        builder.Property(counterparty => counterparty.UserId).IsRequired();
        builder.Property(counterparty => counterparty.Name).HasMaxLength(200).IsRequired();
        builder.Property(counterparty => counterparty.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(counterparty => counterparty.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(counterparty => counterparty.DeletionCascadeId);
        builder.Property(counterparty => counterparty.CreatedAt).IsRequired();
        builder.Property(counterparty => counterparty.UpdatedAt).IsRequired();
        builder.HasIndex(counterparty => counterparty.PublicId).IsUnique();
        builder.HasIndex(counterparty => new
        {
            counterparty.UserId,
            counterparty.NormalizedName
        })
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(counterparty => new { counterparty.UserId, counterparty.IsDeleted });
        builder.HasOne(counterparty => counterparty.User)
            .WithMany()
            .HasForeignKey(counterparty => counterparty.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
