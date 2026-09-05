using ArturRios.Fortuna.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class ImportJobMap : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_job", table =>
        {
            table.HasCheckConstraint(
                "ck_import_job_source_type",
                "source_type BETWEEN 2 AND 4");
            table.HasCheckConstraint(
                "ck_import_job_status",
                "status BETWEEN 1 AND 4");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.PublicId).IsRequired();
        builder.Property(job => job.UserId).IsRequired();
        builder.Property(job => job.SourceType).IsRequired();
        builder.Property(job => job.Status).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.UpdatedAt).IsRequired();
        builder.HasIndex(job => job.PublicId).IsUnique();
        builder.HasIndex(job => new { job.UserId, job.Status });
        builder.HasOne(job => job.User)
            .WithMany()
            .HasForeignKey(job => job.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImportedRecordMap : IEntityTypeConfiguration<ImportedRecord>
{
    public void Configure(EntityTypeBuilder<ImportedRecord> builder)
    {
        builder.ToTable("imported_record", table =>
        {
            table.HasCheckConstraint(
                "ck_imported_record_outcome",
                "outcome BETWEEN 1 AND 3");
            table.HasCheckConstraint(
                "ck_imported_record_amount",
                "amount IS NULL OR amount > 0");
        });
        builder.HasKey(record => record.Id);
        builder.Property(record => record.ImportJobId).IsRequired();
        builder.Property(record => record.RawPayload).HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.ExternalId).HasMaxLength(200);
        builder.Property(record => record.Outcome).IsRequired();
        builder.Property(record => record.RejectionReason).HasMaxLength(1000);
        builder.Property(record => record.Amount).HasPrecision(19, 4);
        builder.Property(record => record.OccurredOn);
        builder.HasIndex(record => record.ImportJobId);
        builder.HasOne(record => record.ImportJob)
            .WithMany(job => job.Records)
            .HasForeignKey(record => record.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
