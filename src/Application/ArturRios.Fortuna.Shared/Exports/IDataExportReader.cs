using ArturRios.Fortuna.Domain.Exports;

namespace ArturRios.Fortuna.Shared.Exports;

public sealed record DataExportReadSnapshot(
    Guid Id,
    Guid? BackgroundJobId,
    DataExportFormat Format,
    DataExportStatus Status,
    string FileName,
    int? RowCount,
    string? ContentType,
    string? StorageKey,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public interface IDataExportReader
{
    Task<DataExportReadSnapshot?> FindOwnedAsync(
        Guid userId,
        Guid exportId,
        CancellationToken cancellationToken);
}
