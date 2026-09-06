using ArturRios.Fortuna.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class ConnectionMap : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.ToTable("connection");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.PublicId).IsRequired();
        builder.Property(connection => connection.UserId).IsRequired();
        builder.Property(connection => connection.DataSourceType).IsRequired();
        builder.Property(connection => connection.ExternalReference).HasMaxLength(200).IsRequired();
        builder.Property(connection => connection.AccessTokenCipher).IsRequired();
        builder.Property(connection => connection.Status).IsRequired();
        builder.Property(connection => connection.CreatedAt).IsRequired();
        builder.Property(connection => connection.UpdatedAt).IsRequired();
        builder.HasIndex(connection => connection.PublicId).IsUnique();
        builder.HasIndex(connection => new
        {
            connection.UserId,
            connection.DataSourceType,
            connection.ExternalReference
        }).IsUnique();
        builder.HasOne(connection => connection.User)
            .WithMany()
            .HasForeignKey(connection => connection.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
