using ArturRios.Fortuna.Domain.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class DataExportMap : IEntityTypeConfiguration<DataExport>
{
    public void Configure(EntityTypeBuilder<DataExport> builder)
    {
        builder.ToTable("data_export", table =>
        {
            table.HasCheckConstraint("ck_data_export_format", "format BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_data_export_status", "status BETWEEN 1 AND 4");
            table.HasCheckConstraint("ck_data_export_row_count", "row_count IS NULL OR row_count >= 0");
        });
        builder.HasKey(export => export.Id);
        builder.Property(export => export.PublicId).IsRequired();
        builder.Property(export => export.UserId).IsRequired();
        builder.Property(export => export.BackgroundJobId).IsRequired();
        builder.Property(export => export.Format).IsRequired();
        builder.Property(export => export.Status).IsRequired();
        builder.Property(export => export.Locale).HasMaxLength(35).IsRequired();
        builder.Property(export => export.FileName).HasMaxLength(300).IsRequired();
        builder.Property(export => export.RequestJson).HasColumnType("jsonb").IsRequired();
        builder.Property(export => export.RowCount);
        builder.Property(export => export.ContentType).HasMaxLength(150);
        builder.Property(export => export.StorageKey).HasMaxLength(500);
        builder.Property(export => export.FailureReason).HasMaxLength(1000);
        builder.Property(export => export.CreatedAt).IsRequired();
        builder.Property(export => export.UpdatedAt).IsRequired();
        builder.Property(export => export.ExpiresAt).IsRequired();
        builder.HasIndex(export => export.PublicId).IsUnique();
        builder.HasIndex(export => export.BackgroundJobId).IsUnique();
        builder.HasIndex(export => new { export.UserId, export.CreatedAt });
        builder.HasOne(export => export.User)
            .WithMany()
            .HasForeignKey(export => export.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(export => export.BackgroundJob)
            .WithOne()
            .HasForeignKey<DataExport>(export => export.BackgroundJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
