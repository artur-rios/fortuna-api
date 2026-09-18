using ArturRios.Fortuna.Domain.Currencies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class ExchangeRateMap : IEntityTypeConfiguration<ExchangeRate>
{
    public const int RateScale = 8;

    // Rates are stored with eight decimal places on every provider; rounding before comparing
    // and storing keeps a re-synchronized rate from looking changed and returns what is stored.
    public static decimal RoundToStorage(decimal rate) =>
        decimal.Round(rate, RateScale, MidpointRounding.AwayFromZero);

    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rate");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Rate).HasPrecision(19, RateScale).IsRequired();
        builder.Property(x => x.RateDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.Source).IsRequired();
        builder.HasOne(x => x.BaseCurrency).WithMany().HasForeignKey(x => x.BaseCurrencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.QuoteCurrency).WithMany().HasForeignKey(x => x.QuoteCurrencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BaseCurrencyId, x.QuoteCurrencyId, x.RateDate, x.Source }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint("ck_exchange_rate_positive", "rate > 0"));
        builder.ToTable(table => table.HasCheckConstraint("ck_exchange_rate_distinct_currency", "base_currency_id <> quote_currency_id"));
    }
}
