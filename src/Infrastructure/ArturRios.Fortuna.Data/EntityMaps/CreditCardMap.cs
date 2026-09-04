using ArturRios.Fortuna.Domain.Cards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class CreditCardMap : IEntityTypeConfiguration<CreditCard>
{
    public const string LiveNameIndex = "ux_credit_card_user_normalized_name_live";

    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable("credit_card", table =>
        {
            table.HasCheckConstraint("ck_credit_card_limit", "credit_limit > 0");
            table.HasCheckConstraint("ck_credit_card_closing_day", "closing_day BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_credit_card_due_day", "due_day BETWEEN 1 AND 31");
            table.HasCheckConstraint(
                "ck_credit_card_last_four_digits",
                "last_four_digits IS NULL OR last_four_digits ~ '^[0-9]{4}$'");
            table.HasCheckConstraint(
                "ck_credit_card_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(card => card.Id);
        builder.Property(card => card.PublicId).IsRequired();
        builder.Property(card => card.UserId).IsRequired();
        builder.Property(card => card.Name).HasMaxLength(200).IsRequired();
        builder.Property(card => card.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(card => card.Issuer).HasMaxLength(200).IsRequired();
        builder.Property(card => card.CurrencyId).IsRequired();
        builder.Property(card => card.CreditLimit).HasPrecision(19, 4).IsRequired();
        builder.Property(card => card.ClosingDay).IsRequired();
        builder.Property(card => card.DueDay).IsRequired();
        builder.Property(card => card.LastFourDigits).HasColumnType("char(4)");
        builder.Property(card => card.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(card => card.DeletionCascadeId);
        builder.Property(card => card.CreatedAt).IsRequired();
        builder.Property(card => card.UpdatedAt).IsRequired();
        builder.HasIndex(card => card.PublicId).IsUnique();
        builder.HasIndex(card => new { card.UserId, card.NormalizedName })
            .HasDatabaseName(LiveNameIndex)
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(card => new { card.UserId, card.IsDeleted });
        builder.HasOne(card => card.User)
            .WithMany()
            .HasForeignKey(card => card.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(card => card.Currency)
            .WithMany()
            .HasForeignKey(card => card.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
