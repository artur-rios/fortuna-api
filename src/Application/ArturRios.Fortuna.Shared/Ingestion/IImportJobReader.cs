using ArturRios.Fortuna.Domain.Ingestion;

namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IImportJobReader
{
    IQueryable<ImportJob> Query();
    IQueryable<ImportedRecord> Records();
    Task<ImportJobReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
    Task<bool> IsOwnedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public sealed record ImportJobReadSnapshot(
    Guid Id,
    Guid? ConnectionId,
    Domain.Transactions.TransactionSourceType SourceType,
    ImportJobStatus Status,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ImportedCount,
    int DuplicateCount,
    int RejectedCount,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
