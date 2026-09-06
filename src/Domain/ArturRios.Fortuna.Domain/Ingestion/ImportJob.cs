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
        : this(user, null, sourceType, null, null, createdAt)
    {
    }

    public ImportJob(
        UserProfile user,
        Connection connection,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        DateTimeOffset createdAt)
        : this(user, connection, connection?.DataSourceType ?? TransactionSourceType.Manual,
            periodStart, periodEnd, createdAt)
    {
    }

    private ImportJob(
        UserProfile user,
        Connection? connection,
        TransactionSourceType sourceType,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        DateTimeOffset createdAt)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        if (sourceType == TransactionSourceType.Manual || !Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        if (connection is not null && connection.User.PublicId != user.PublicId)
        {
            throw new ArgumentException(
                "The import job and connection must have the same owner.",
                nameof(connection));
        }

        if (periodStart.HasValue && periodEnd.HasValue && periodStart > periodEnd)
        {
            throw new ArgumentException("The period start cannot follow its end.", nameof(periodStart));
        }

        PublicId = Guid.NewGuid();
        UserId = user.Id;
        Connection = connection;
        ConnectionId = connection?.Id;
        SourceType = sourceType;
        Status = ImportJobStatus.Pending;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long UserId { get; private set; }
    public UserProfile User { get; private set; } = null!;
    public long? ConnectionId { get; private set; }
    public Connection? Connection { get; private set; }
    public TransactionSourceType SourceType { get; private set; }
    public ImportJobStatus Status { get; private set; }
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }
    public int ImportedCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int RejectedCount { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<ImportedRecord> Records { get; } = [];

    public void Start(DateTimeOffset updatedAt)
    {
        if (Status != ImportJobStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending import job can start.");
        }

        Status = ImportJobStatus.Running;
        FailureReason = null;
        UpdatedAt = updatedAt;
    }

    public void Complete(
        int importedCount,
        int duplicateCount,
        int rejectedCount,
        DateTimeOffset updatedAt)
    {
        if (Status != ImportJobStatus.Running)
        {
            throw new InvalidOperationException("Only a running import job can complete.");
        }

        if (importedCount < 0 || duplicateCount < 0 || rejectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(importedCount));
        }

        ImportedCount = importedCount;
        DuplicateCount = duplicateCount;
        RejectedCount = rejectedCount;
        FailureReason = null;
        Status = ImportJobStatus.Completed;
        UpdatedAt = updatedAt;
    }

    public void Fail(string reason, DateTimeOffset updatedAt)
    {
        if (Status is not (ImportJobStatus.Pending or ImportJobStatus.Running))
        {
            throw new InvalidOperationException("Only an unfinished import job can fail.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
        {
            throw new ArgumentException(
                "A failure reason between 1 and 1000 characters is required.",
                nameof(reason));
        }

        FailureReason = reason.Trim();
        Status = ImportJobStatus.Failed;
        UpdatedAt = updatedAt;
    }
}

public sealed class ConnectionResource
{
    private ConnectionResource()
    {
    }

    public ConnectionResource(
        Connection connection,
        string externalReference,
        Accounts.FinancialAccount? account = null,
        Cards.CreditCard? card = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Trim().Length > 200)
        {
            throw new ArgumentException(
                "An external reference between 1 and 200 characters is required.",
                nameof(externalReference));
        }

        if ((account is null) == (card is null))
        {
            throw new ArgumentException("Exactly one mapped resource is required.");
        }

        var owner = account?.User ?? card!.User;
        if (owner.PublicId != connection.User.PublicId)
        {
            throw new ArgumentException(
                "The connection and mapped resource must have the same owner.");
        }

        ConnectionId = connection.Id;
        ExternalReference = externalReference.Trim();
        FinancialAccount = account;
        FinancialAccountId = account?.Id;
        CreditCard = card;
        CreditCardId = card?.Id;
    }

    public long Id { get; private set; }
    public long ConnectionId { get; private set; }
    public Connection Connection { get; private set; } = null!;
    public string ExternalReference { get; private set; } = string.Empty;
    public long? FinancialAccountId { get; private set; }
    public Accounts.FinancialAccount? FinancialAccount { get; private set; }
    public long? CreditCardId { get; private set; }
    public Cards.CreditCard? CreditCard { get; private set; }
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
