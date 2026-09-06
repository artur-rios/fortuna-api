using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class BudgetMap : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budget", table =>
        {
            table.HasCheckConstraint("ck_budget_amount", "amount > 0");
            table.HasCheckConstraint("ck_budget_period_type", "period_type BETWEEN 1 AND 3");
            table.HasCheckConstraint(
                "ck_budget_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(budget => budget.Id);
        builder.Property(budget => budget.PublicId).IsRequired();
        builder.Property(budget => budget.UserId).IsRequired();
        builder.Property(budget => budget.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(budget => budget.CurrencyId).IsRequired();
        builder.Property(budget => budget.PeriodType).IsRequired();
        builder.Property(budget => budget.PeriodStart).IsRequired();
        builder.Property(budget => budget.IncludeDescendants).HasDefaultValue(true).IsRequired();
        builder.Property(budget => budget.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(budget => budget.DeletionCascadeId);
        builder.Property(budget => budget.CreatedAt).IsRequired();
        builder.Property(budget => budget.UpdatedAt).IsRequired();
        builder.HasIndex(budget => budget.PublicId).IsUnique();
        builder.HasIndex(budget => new { budget.UserId, budget.IsDeleted });
        builder.HasOne(budget => budget.User)
            .WithMany()
            .HasForeignKey(budget => budget.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(budget => budget.Currency)
            .WithMany()
            .HasForeignKey(budget => budget.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(budget => budget.Categories)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "BudgetCategory",
                right => right
                    .HasOne<Category>()
                    .WithMany()
                    .HasForeignKey("CategoryId")
                    .OnDelete(DeleteBehavior.Restrict),
                left => left
                    .HasOne<Budget>()
                    .WithMany()
                    .HasForeignKey("BudgetId")
                    .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("budget_category");
                    join.HasKey("BudgetId", "CategoryId");
                    join.HasIndex("CategoryId");
                });
    }
}
