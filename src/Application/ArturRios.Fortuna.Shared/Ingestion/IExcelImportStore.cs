using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Ingestion;

public enum ImportTargetType
{
    Account = 1,
    CreditCard = 2
}

public sealed record ExcelColumnMapping(
    string Date,
    string Amount,
    string Direction,
    string? Description,
    string? Category,
    string? ExternalId);

public sealed record ExcelImportRequest(
    Guid UserId,
    Guid TargetId,
    ImportTargetType TargetType,
    string FileName,
    byte[] Content,
    ExcelColumnMapping Mapping,
    bool CreateMissingCategories,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

public enum QueueExcelImportOutcome
{
    Succeeded = 1,
    TargetNotFound = 2,
    TargetDeleted = 3
}

public sealed record QueueExcelImportResult(
    ExcelImportJobSnapshot? Job,
    Guid? BackgroundJobId,
    QueueExcelImportOutcome Outcome);

public sealed record ExcelImportJobSnapshot(
    Guid Id,
    ImportJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IExcelImportStore
{
    Task<QueueExcelImportResult> QueueAsync(
        ExcelImportRequest request,
        CancellationToken cancellationToken);

    Task<bool> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid importJobId,
        Guid userId,
        Guid targetId,
        ImportTargetType targetType,
        bool createMissingCategories,
        IReadOnlyCollection<ExcelWorkbookRow> rows,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid importJobId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

public interface IExcelWorkbookParser
{
    ExcelWorkbookValidation Validate(byte[] content, ExcelColumnMapping mapping);
    IReadOnlyCollection<ExcelWorkbookRow> Parse(byte[] content, ExcelColumnMapping mapping);
}

public sealed record ExcelWorkbookValidation(bool IsValid, string? Error);

public sealed record ExcelWorkbookRow(
    int RowNumber,
    string RawPayload,
    DateOnly? OccurredOn,
    decimal? Amount,
    TransactionDirection? Direction,
    string? Description,
    string? Category,
    string? ExternalId,
    string? RejectionReason);

public static class ExcelImportJob
{
    public const string Type = "excel-import";
}

public sealed record ExcelImportJobPayload(
    Guid ImportJobId,
    Guid UserId,
    Guid TargetId,
    ImportTargetType TargetType,
    byte[] Content,
    ExcelColumnMapping Mapping,
    bool CreateMissingCategories);

public sealed record ExcelImportOptions(int MaximumFileBytes);
