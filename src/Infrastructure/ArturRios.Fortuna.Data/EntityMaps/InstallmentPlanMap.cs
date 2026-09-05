using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class InstallmentPlanMap : IEntityTypeConfiguration<InstallmentPlan>
{
    public void Configure(EntityTypeBuilder<InstallmentPlan> builder)
    {
        builder.ToTable("installment_plan", table =>
        {
            table.HasCheckConstraint("ck_installment_plan_total_amount", "total_amount > 0");
            table.HasCheckConstraint("ck_installment_plan_count", "installment_count >= 2");
            table.HasCheckConstraint(
                "ck_installment_plan_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.PublicId).IsRequired();
        builder.Property(plan => plan.CreditCardId).IsRequired();
        builder.Property(plan => plan.TotalAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(plan => plan.InstallmentCount).IsRequired();
        builder.Property(plan => plan.PurchasedOn).IsRequired();
        builder.Property(plan => plan.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(plan => plan.DeletionCascadeId);
        builder.Property(plan => plan.CreatedAt).IsRequired();
        builder.Property(plan => plan.UpdatedAt).IsRequired();
        builder.HasIndex(plan => plan.PublicId).IsUnique();
        builder.HasIndex(plan => new { plan.CreditCardId, plan.IsDeleted });
        builder.HasOne(plan => plan.CreditCard)
            .WithMany()
            .HasForeignKey(plan => plan.CreditCardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
