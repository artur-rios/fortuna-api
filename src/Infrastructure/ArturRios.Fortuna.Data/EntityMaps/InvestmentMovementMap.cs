using ArturRios.Fortuna.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class InvestmentMovementMap : IEntityTypeConfiguration<InvestmentMovement>
{
    public void Configure(EntityTypeBuilder<InvestmentMovement> builder)
    {
        builder.ToTable("investment_movement", table =>
        {
            table.HasCheckConstraint(
                "ck_investment_movement_type",
                "movement_type BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_investment_movement_amount",
                "amount > 0");
            table.HasCheckConstraint(
                "ck_investment_movement_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.PublicId).IsRequired();
        builder.Property(movement => movement.InvestmentId).IsRequired();
        builder.Property(movement => movement.MovementType).IsRequired();
        builder.Property(movement => movement.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(movement => movement.OccurredOn).IsRequired();
        builder.Property(movement => movement.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(movement => movement.DeletionCascadeId);
        builder.Property(movement => movement.CreatedAt).IsRequired();
        builder.Property(movement => movement.UpdatedAt).IsRequired();
        builder.HasIndex(movement => movement.PublicId).IsUnique();
        builder.HasIndex(movement => new
        { movement.InvestmentId, movement.IsDeleted, movement.OccurredOn });
        builder.HasOne(movement => movement.Investment)
            .WithMany()
            .HasForeignKey(movement => movement.InvestmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
