using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Shared.Reporting;

namespace ArturRios.Fortuna.Shared.Exports;

public sealed record DataExportOptions(
    int SynchronousThresholdRows,
    TimeSpan Retention,
    string DefaultLocale);

public sealed record DataExportFilter(string Field, string Operator, string Value);

public sealed record DataExportSort(string Field, bool Descending);

public sealed record DataExportSpecification(
    string RecordSet,
    IReadOnlyCollection<string> Columns,
    IReadOnlyCollection<DataExportFilter> Filters,
    IReadOnlyCollection<DataExportSort> Sorts,
    string? DisplayCurrencyCode,
    DataExportFormat Format,
    string Locale);

public sealed record QueueDataExportRequest(
    Guid UserId,
    DataExportSpecification Specification,
    string FileName,
    string? CorrelationId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt);

public sealed record QueueDataExportResult(Guid ExportId, Guid BackgroundJobId);

public sealed record DataExportWorkItem(
    Guid ExportId,
    Guid UserId,
    DataExportSpecification Specification,
    string FileName);

public interface IDataExportStore
{
    Task<QueueDataExportResult> QueueAsync(
        QueueDataExportRequest request,
        CancellationToken cancellationToken);

    Task<DataExportWorkItem?> StartAsync(
        Guid exportId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid exportId,
        int rowCount,
        string contentType,
        string storageKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid exportId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

public interface IDataExportRenderer
{
    RenderedDataExport Render(DataExportDocument document, DataExportFormat format);
}

public sealed record DataExportDocument(
    string RecordSet,
    IReadOnlyCollection<TableColumnSnapshot> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyCollection<DataExportTotal> Totals,
    string Locale);

public sealed record DataExportTotal(
    string Column,
    string? CurrencyCode,
    decimal? Value,
    bool IsFullyConverted);

public sealed record RenderedDataExport(
    byte[] Content,
    string ContentType,
    string Extension);

public static class DataExportJob
{
    public const string Type = "data-export";
}

public sealed record DataExportJobPayload(Guid ExportId);
