using ArturRios.Fortuna.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArturRios.Fortuna.Data.EntityMaps;

public sealed class AuditSubjectMap : IEntityTypeConfiguration<AuditSubject>
{
    public const string UserIndex = "ix_audit_subject_user_id";

    public void Configure(EntityTypeBuilder<AuditSubject> builder)
    {
        builder.ToTable("audit_subject");
        builder.HasKey(subject => subject.Id);
        builder.Property(subject => subject.UserId).IsRequired();
        builder.Property(subject => subject.SubjectReference).IsRequired();
        builder.HasIndex(subject => subject.UserId).HasDatabaseName(UserIndex).IsUnique();
        builder.HasIndex(subject => subject.SubjectReference).IsUnique();
        builder.HasOne(subject => subject.User)
            .WithOne()
            .HasForeignKey<AuditSubject>(subject => subject.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
