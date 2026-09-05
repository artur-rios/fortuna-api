using System.Text.Json;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Domain.Ingestion;

public enum ImportJobStatus : short
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}

public enum ImportedRecordOutcome : short
{
    Imported = 1,
    Duplicate = 2,
    Rejected = 3
}

public sealed class ImportJob
{
    private ImportJob()
    {
    }

    public ImportJob(
        UserProfile user,
        TransactionSourceType sourceType,
        DateTimeOffset createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (sourceType == TransactionSourceType.Manual || !Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        PublicId = Guid.NewGuid();
        UserId = user.Id;
        SourceType = sourceType;
        Status = ImportJobStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public TransactionSourceType SourceType { get; private set; }
    public ImportJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<ImportedRecord> Records { get; } = [];
}

public sealed class ImportedRecord
{
    private ImportedRecord()
    {
    }

    public ImportedRecord(
        ImportJob importJob,
        string rawPayload,
        ImportedRecordOutcome outcome,
        decimal? amount,
        DateOnly? occurredOn,
        string? externalId = null,
        string? rejectionReason = null)
    {
        ImportJob = importJob ?? throw new ArgumentNullException(nameof(importJob));
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new ArgumentException("A raw payload is required.", nameof(rawPayload));
        }

        using var _ = JsonDocument.Parse(rawPayload);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (amount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (externalId?.Length > 200)
        {
            throw new ArgumentException(
                "An external identifier cannot exceed 200 characters.",
                nameof(externalId));
        }

        if (rejectionReason?.Length > 1000)
        {
            throw new ArgumentException(
                "A rejection reason cannot exceed 1000 characters.",
                nameof(rejectionReason));
        }

        ImportJobId = importJob.Id;
        RawPayload = rawPayload;
        ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        Outcome = outcome;
        RejectionReason = string.IsNullOrWhiteSpace(rejectionReason)
            ? null
            : rejectionReason.Trim();
        Amount = amount;
        OccurredOn = occurredOn;
    }

    public long Id { get; private set; }
    public long ImportJobId { get; private set; }
    public ImportJob ImportJob { get; private set; } = null!;
    public string RawPayload { get; private set; } = string.Empty;
    public string? ExternalId { get; private set; }
    public ImportedRecordOutcome Outcome { get; private set; }
    public string? RejectionReason { get; private set; }
    public decimal? Amount { get; private set; }
    public DateOnly? OccurredOn { get; private set; }
    public FinancialTransaction? Transaction { get; private set; }
}
