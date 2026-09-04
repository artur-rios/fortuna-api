using ArturRios.Fortuna.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class FinancialAccountMap : IEntityTypeConfiguration<FinancialAccount>
{
    public const string LiveNameIndex = "ux_financial_account_user_normalized_name_live";

    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_account", table =>
        {
            table.HasCheckConstraint(
                "ck_financial_account_type",
                "account_type BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_financial_account_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(account => account.Id);
        builder.Property(account => account.PublicId).IsRequired();
        builder.Property(account => account.UserId).IsRequired();
        builder.Property(account => account.Name).HasMaxLength(200).IsRequired();
        builder.Property(account => account.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(account => account.Institution).HasMaxLength(200);
        builder.Property(account => account.AccountType).IsRequired();
        builder.Property(account => account.CurrencyId).IsRequired();
        builder.Property(account => account.OpeningBalance).HasPrecision(19, 4).IsRequired();
        builder.Property(account => account.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(account => account.DeletionCascadeId);
        builder.Property(account => account.CreatedAt).IsRequired();
        builder.Property(account => account.UpdatedAt).IsRequired();
        builder.HasIndex(account => account.PublicId).IsUnique();
        builder.HasIndex(account => new { account.UserId, account.NormalizedName })
            .HasDatabaseName(LiveNameIndex)
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(account => new { account.UserId, account.IsDeleted });
        builder.HasOne(account => account.User)
            .WithMany()
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(account => account.Currency)
            .WithMany()
            .HasForeignKey(account => account.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
