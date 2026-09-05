using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class FinancialTransactionMap : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("financial_transaction", table =>
        {
            table.HasCheckConstraint(
                "ck_financial_transaction_direction",
                "direction BETWEEN 1 AND 2");
            table.HasCheckConstraint(
                "ck_financial_transaction_amount",
                "amount > 0");
            table.HasCheckConstraint(
                "ck_financial_transaction_source_type",
                "source_type BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_financial_transaction_foreign_currency",
                "(original_amount IS NULL AND original_currency_id IS NULL AND " +
                "applied_rate IS NULL AND rate_date IS NULL) OR " +
                "(original_amount > 0 AND original_currency_id IS NOT NULL AND " +
                "applied_rate > 0 AND rate_date IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_financial_transaction_target",
                "(financial_account_id IS NOT NULL AND credit_card_id IS NULL) OR " +
                "(financial_account_id IS NULL AND credit_card_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_financial_transaction_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.PublicId).IsRequired();
        builder.Property(transaction => transaction.UserId).IsRequired();
        builder.Property(transaction => transaction.FinancialAccountId);
        builder.Property(transaction => transaction.CreditCardId);
        builder.Property(transaction => transaction.StatementId);
        builder.Property(transaction => transaction.CategoryId).IsRequired();
        builder.Property(transaction => transaction.CounterpartyId);
        builder.Property(transaction => transaction.Direction).IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(transaction => transaction.CurrencyId).IsRequired();
        builder.Property(transaction => transaction.OriginalAmount).HasPrecision(19, 4);
        builder.Property(transaction => transaction.OriginalCurrencyId);
        builder.Property(transaction => transaction.AppliedRate).HasPrecision(19, 8);
        builder.Property(transaction => transaction.RateDate);
        builder.Property(transaction => transaction.OccurredOn).IsRequired();
        builder.Property(transaction => transaction.Description).HasMaxLength(500);
        builder.Property(transaction => transaction.SourceType)
            .HasDefaultValue(TransactionSourceType.Manual)
            .IsRequired();
        builder.Property(transaction => transaction.IsReconciled)
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(transaction => transaction.IsLateArriving).HasDefaultValue(false).IsRequired();
        builder.Property(transaction => transaction.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(transaction => transaction.DeletionCascadeId);
        builder.Property(transaction => transaction.CreatedAt).IsRequired();
        builder.Property(transaction => transaction.UpdatedAt).IsRequired();
        builder.HasIndex(transaction => transaction.PublicId).IsUnique();
        builder.HasIndex(transaction => new
        {
            transaction.FinancialAccountId,
            transaction.IsDeleted,
            transaction.OccurredOn
        });
        builder.HasIndex(transaction => new
        {
            transaction.CreditCardId,
            transaction.IsDeleted,
            transaction.OccurredOn
        });
        builder.HasIndex(transaction => new { transaction.UserId, transaction.IsDeleted });
        builder.HasOne(transaction => transaction.User)
            .WithMany()
            .HasForeignKey(transaction => transaction.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.FinancialAccount)
            .WithMany()
            .HasForeignKey(transaction => transaction.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.CreditCard)
            .WithMany()
            .HasForeignKey(transaction => transaction.CreditCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Statement)
            .WithMany()
            .HasForeignKey(transaction => transaction.StatementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Category)
            .WithMany()
            .HasForeignKey(transaction => transaction.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Counterparty)
            .WithMany()
            .HasForeignKey(transaction => transaction.CounterpartyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Currency)
            .WithMany()
            .HasForeignKey(transaction => transaction.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.OriginalCurrency)
            .WithMany()
            .HasForeignKey(transaction => transaction.OriginalCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(transaction => transaction.Tags)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "FinancialTransactionTag",
                right => right
                    .HasOne<ArturRios.Fortuna.Domain.Classification.Tag>()
                    .WithMany()
                    .HasForeignKey("TagId")
                    .OnDelete(DeleteBehavior.Restrict),
                left => left
                    .HasOne<FinancialTransaction>()
                    .WithMany()
                    .HasForeignKey("FinancialTransactionId")
                    .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("financial_transaction_tag");
                    join.HasKey("FinancialTransactionId", "TagId");
                    join.HasIndex("TagId");
                });
    }
}
