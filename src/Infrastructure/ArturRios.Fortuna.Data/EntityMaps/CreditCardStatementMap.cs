using ArturRios.Fortuna.Domain.Cards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class CreditCardStatementMap : IEntityTypeConfiguration<CreditCardStatement>
{
    public const string CycleIndex = "ux_credit_card_statement_card_period";

    public void Configure(EntityTypeBuilder<CreditCardStatement> builder)
    {
        builder.ToTable("credit_card_statement", table =>
        {
            table.HasCheckConstraint(
                "ck_credit_card_statement_period",
                "period_start <= period_end AND closing_date = period_end AND due_date > closing_date");
            table.HasCheckConstraint(
                "ck_credit_card_statement_status",
                "status BETWEEN 1 AND 3");
            table.HasCheckConstraint(
                "ck_credit_card_statement_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(statement => statement.Id);
        builder.Property(statement => statement.PublicId).IsRequired();
        builder.Property(statement => statement.CreditCardId).IsRequired();
        builder.Property(statement => statement.PeriodStart).IsRequired();
        builder.Property(statement => statement.PeriodEnd).IsRequired();
        builder.Property(statement => statement.ClosingDate).IsRequired();
        builder.Property(statement => statement.DueDate).IsRequired();
        builder.Property(statement => statement.PreviousBalance).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.PaymentsReceived).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.PurchaseTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.ForeignTaxTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.OtherEntries).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.AmountDue).HasPrecision(19, 4).IsRequired();
        builder.Property(statement => statement.Status).IsRequired();
        builder.Property(statement => statement.SettlementTransactionId);
        builder.Property(statement => statement.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(statement => statement.DeletionCascadeId);
        builder.Property(statement => statement.CreatedAt).IsRequired();
        builder.Property(statement => statement.UpdatedAt).IsRequired();
        builder.HasIndex(statement => statement.PublicId).IsUnique();
        builder.HasIndex(statement => new
        { statement.CreditCardId, statement.PeriodStart, statement.PeriodEnd })
            .HasDatabaseName(CycleIndex)
            .IsUnique();
        builder.HasIndex(statement => new { statement.CreditCardId, statement.Status });
        builder.HasOne(statement => statement.CreditCard)
            .WithMany()
            .HasForeignKey(statement => statement.CreditCardId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(statement => statement.SettlementTransaction)
            .WithMany()
            .HasForeignKey(statement => statement.SettlementTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
