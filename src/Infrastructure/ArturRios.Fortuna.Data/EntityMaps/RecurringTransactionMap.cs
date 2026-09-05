using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class RecurringTransactionMap : IEntityTypeConfiguration<RecurringTransaction>
{
    public void Configure(EntityTypeBuilder<RecurringTransaction> builder)
    {
        builder.ToTable("recurring_transaction", table =>
        {
            table.HasCheckConstraint("ck_recurring_transaction_amount", "amount > 0");
            table.HasCheckConstraint("ck_recurring_transaction_direction", "direction BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_recurring_transaction_frequency", "frequency BETWEEN 1 AND 4");
            table.HasCheckConstraint("ck_recurring_transaction_dates", "ends_on IS NULL OR ends_on >= starts_on");
            table.HasCheckConstraint(
                "ck_recurring_transaction_target",
                "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR " +
                "(financial_account_id IS NULL AND credit_card_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_recurring_transaction_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.PublicId).IsRequired();
        builder.Property(rule => rule.UserId).IsRequired();
        builder.Property(rule => rule.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(rule => rule.Description).HasMaxLength(500);
        builder.Property(rule => rule.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(rule => rule.CreatedAt).IsRequired();
        builder.Property(rule => rule.UpdatedAt).IsRequired();
        builder.HasIndex(rule => rule.PublicId).IsUnique();
        builder.HasIndex(rule => new { rule.UserId, rule.IsDeleted });
        builder.HasOne(rule => rule.User).WithMany().HasForeignKey(rule => rule.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.FinancialAccount).WithMany().HasForeignKey(rule => rule.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.CreditCard).WithMany().HasForeignKey(rule => rule.CreditCardId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Category).WithMany().HasForeignKey(rule => rule.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Counterparty).WithMany().HasForeignKey(rule => rule.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(rule => rule.Currency).WithMany().HasForeignKey(rule => rule.CurrencyId).OnDelete(DeleteBehavior.Restrict);
    }
}
