using ArturRios.Fortuna.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class InvestmentValuationMap : IEntityTypeConfiguration<InvestmentValuation>
{
    public const string LiveValuationDateIndex =
        "ux_investment_valuation_investment_valued_on_live";

    public void Configure(EntityTypeBuilder<InvestmentValuation> builder)
    {
        builder.ToTable("investment_valuation", table =>
        {
            table.HasCheckConstraint(
                "ck_investment_valuation_deletion_state",
                "(is_deleted AND deletion_cascade_id IS NOT NULL) OR " +
                "(NOT is_deleted AND deletion_cascade_id IS NULL)");
        });
        builder.HasKey(valuation => valuation.Id);
        builder.Property(valuation => valuation.PublicId).IsRequired();
        builder.Property(valuation => valuation.InvestmentId).IsRequired();
        builder.Property(valuation => valuation.Value).HasPrecision(19, 4).IsRequired();
        builder.Property(valuation => valuation.ValuedOn).IsRequired();
        builder.Property(valuation => valuation.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(valuation => valuation.DeletionCascadeId);
        builder.Property(valuation => valuation.CreatedAt).IsRequired();
        builder.Property(valuation => valuation.UpdatedAt).IsRequired();
        builder.HasIndex(valuation => valuation.PublicId).IsUnique();
        builder.HasIndex(valuation => new { valuation.InvestmentId, valuation.ValuedOn })
            .HasDatabaseName(LiveValuationDateIndex)
            .IsUnique()
            .HasFilter("NOT is_deleted");
        builder.HasIndex(valuation => new
        { valuation.InvestmentId, valuation.IsDeleted, valuation.ValuedOn });
        builder.HasOne(valuation => valuation.Investment)
            .WithMany()
            .HasForeignKey(valuation => valuation.InvestmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
