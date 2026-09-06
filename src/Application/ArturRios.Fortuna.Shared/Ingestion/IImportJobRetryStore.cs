using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IImportJobRetryStore
{
    Task<RetryImportJobResult> RetryAsync(
        Guid userId,
        Guid importJobId,
        DateTimeOffset retriedAt,
        CancellationToken cancellationToken);
}

public enum RetryImportJobOutcome
{
    Succeeded,
    NotFound,
    NotFailed,
    SourceFileNotRetained
}

public sealed record RetryImportJobSnapshot(
    Guid Id,
    TransactionSourceType SourceType,
    ImportJobStatus Status,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ImportedCount,
    int DuplicateCount,
    int RejectedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RetryImportJobResult(
    RetryImportJobOutcome Outcome,
    RetryImportJobSnapshot? Job = null,
    Guid? BackgroundJobId = null);
