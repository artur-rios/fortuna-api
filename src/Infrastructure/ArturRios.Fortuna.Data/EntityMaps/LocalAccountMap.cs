using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class LocalAccountMap : IEntityTypeConfiguration<LocalAccount>
{
    public const string UserIndex = "ix_local_account_user_id";
    public const string NameIndex = "ix_local_account_name";

    public void Configure(EntityTypeBuilder<LocalAccount> builder)
    {
        builder.ToTable("local_account");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SecretHash).IsRequired();
        builder.Property(x => x.Salt).IsRequired();
        builder.Property(x => x.StorageMode).HasColumnType("smallint").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.PublicId).IsUnique();
        builder.HasIndex(x => x.UserId).HasDatabaseName(UserIndex).IsUnique();
        builder.HasIndex(x => x.Name).HasDatabaseName(NameIndex).IsUnique();
        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<LocalAccount>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RecoveryCodes)
            .WithOne(x => x.LocalAccount)
            .HasForeignKey(x => x.LocalAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
