using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class RecoveryCodeMap : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("recovery_code");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).IsRequired();
        builder.Property(x => x.UsedAt);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.LocalAccountId, x.CodeHash }).IsUnique();
    }
}
