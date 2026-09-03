using ArturRios.Fortuna.Domain.Currencies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class CurrencyMap : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currency");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasColumnType("char(3)").IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MinorUnitDigits).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
