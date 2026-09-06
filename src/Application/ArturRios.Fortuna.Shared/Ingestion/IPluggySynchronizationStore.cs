using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IPluggySynchronizationStore
{
    Task<QueueSynchronizationResult> QueueAsync(
        Guid userId,
        Guid connectionId,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        string? correlationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task<PluggySynchronizationContext?> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid importJobId,
        PluggySynchronizationBatch batch,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid importJobId,
        string reason,
        bool requiresReauthentication,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

public enum QueueSynchronizationOutcome
{
    Succeeded = 1,
    ConnectionNotFound = 2,
    ConnectionInactive = 3,
    AlreadyRunning = 4,
    ConnectionRequiresReauthentication = 5,
    ConnectionRevoked = 6
}

public sealed record QueueSynchronizationResult(
    ImportJobSnapshot? Job,
    Guid? BackgroundJobId,
    QueueSynchronizationOutcome Outcome);

public sealed record ImportJobSnapshot(
    Guid Id,
    Guid ConnectionId,
    TransactionSourceType SourceType,
    ImportJobStatus Status,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ImportedCount,
    int DuplicateCount,
    int RejectedCount,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PluggySynchronizationContext(
    Guid ImportJobId,
    Guid ConnectionId,
    string ExternalReference,
    byte[] AccessTokenCipher,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd);

public enum PluggyResourceKind
{
    Account = 1,
    CreditCard = 2
}

public sealed record PluggySynchronizationBatch(
    IReadOnlyCollection<PluggyResourceRecord> Resources,
    IReadOnlyCollection<PluggyTransactionRecord> Transactions);

public sealed record PluggyResourceRecord(
    string ExternalReference,
    PluggyResourceKind Kind,
    string Name,
    string Institution,
    string CurrencyCode,
    decimal Balance,
    decimal? CreditLimit,
    short? ClosingDay,
    short? DueDay,
    string? LastFourDigits);

public sealed record PluggyTransactionRecord(
    string RawPayload,
    string AccountExternalReference,
    string? ExternalReference,
    TransactionDirection? Direction,
    decimal? Amount,
    DateOnly? OccurredOn,
    string? Description,
    string? Category);

public interface IPluggySynchronizationGateway
{
    Task<PluggySynchronizationFetchResult> FetchAsync(
        string externalReference,
        string accessToken,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        CancellationToken cancellationToken);
}

public enum PluggySynchronizationFetchOutcome
{
    Succeeded = 1,
    RequiresReauthentication = 2,
    Unavailable = 3
}

public sealed record PluggySynchronizationFetchResult(
    PluggySynchronizationFetchOutcome Outcome,
    PluggySynchronizationBatch? Batch = null);

public static class PluggySynchronizationJob
{
    public const string Type = "pluggy-synchronization";
}

public sealed record PluggySynchronizationJobPayload(Guid ImportJobId);
