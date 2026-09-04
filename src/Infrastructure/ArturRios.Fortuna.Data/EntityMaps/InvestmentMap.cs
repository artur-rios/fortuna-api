using ArturRios.Fortuna.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class InvestmentMap : IEntityTypeConfiguration<Investment>
{
    public const string LiveInstrumentIndex =
        "ux_investment_user_normalized_instrument_live";

    public void Configure(EntityTypeBuilder<Investment> builder)
    {
        builder.ToTable("investment", table =>
        {
            table.HasCheckConstraint(
                "ck_investment_type",
                "investment_type BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_investment_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(investment => investment.Id);
        builder.Property(investment => investment.PublicId).IsRequired();
        builder.Property(investment => investment.UserId).IsRequired();
        builder.Property(investment => investment.Instrument).HasMaxLength(200).IsRequired();
        builder.Property(investment => investment.NormalizedInstrument)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(investment => investment.Institution).HasMaxLength(200);
        builder.Property(investment => investment.InvestmentType).IsRequired();
        builder.Property(investment => investment.CurrencyId).IsRequired();
        builder.Property(investment => investment.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(investment => investment.DeletionCascadeId);
        builder.Property(investment => investment.CreatedAt).IsRequired();
        builder.Property(investment => investment.UpdatedAt).IsRequired();
        builder.HasIndex(investment => investment.PublicId).IsUnique();
        builder.HasIndex(investment => new
        { investment.UserId, investment.NormalizedInstrument })
            .HasDatabaseName(LiveInstrumentIndex)
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(investment => new { investment.UserId, investment.IsDeleted });
        builder.HasOne(investment => investment.User)
            .WithMany()
            .HasForeignKey(investment => investment.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(investment => investment.Currency)
            .WithMany()
            .HasForeignKey(investment => investment.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
