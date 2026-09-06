using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class GoalMap : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goal", table =>
        {
            table.HasCheckConstraint("ck_goal_target_amount", "target_amount > 0");
            table.HasCheckConstraint(
                "ck_goal_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(goal => goal.Id);
        builder.Property(goal => goal.PublicId).IsRequired();
        builder.Property(goal => goal.UserId).IsRequired();
        builder.Property(goal => goal.Name).HasMaxLength(200).IsRequired();
        builder.Property(goal => goal.TargetAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(goal => goal.CurrencyId).IsRequired();
        builder.Property(goal => goal.TargetDate).IsRequired();
        builder.Property(goal => goal.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(goal => goal.DeletionCascadeId);
        builder.Property(goal => goal.CreatedAt).IsRequired();
        builder.Property(goal => goal.UpdatedAt).IsRequired();
        builder.HasIndex(goal => goal.PublicId).IsUnique();
        builder.HasIndex(goal => new { goal.UserId, goal.IsDeleted });
        builder.HasOne(goal => goal.User)
            .WithMany()
            .HasForeignKey(goal => goal.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(goal => goal.Currency)
            .WithMany()
            .HasForeignKey(goal => goal.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(goal => goal.Accounts)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "GoalAccount",
                right => right.HasOne<FinancialAccount>().WithMany()
                    .HasForeignKey("AccountId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<Goal>().WithMany()
                    .HasForeignKey("GoalId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("goal_account");
                    join.HasKey("GoalId", "AccountId");
                    join.HasIndex("AccountId");
                });
        builder.HasMany(goal => goal.Investments)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "GoalInvestment",
                right => right.HasOne<Investment>().WithMany()
                    .HasForeignKey("InvestmentId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<Goal>().WithMany()
                    .HasForeignKey("GoalId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("goal_investment");
                    join.HasKey("GoalId", "InvestmentId");
                    join.HasIndex("InvestmentId");
                });
    }
}
