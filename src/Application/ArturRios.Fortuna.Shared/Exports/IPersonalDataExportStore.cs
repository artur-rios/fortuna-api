using ArturRios.Fortuna.Domain.Exports;

namespace ArturRios.Fortuna.Shared.Exports;

public sealed record QueuePersonalDataExportRequest(
    Guid UserId,
    string FileName,
    string? CorrelationId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt);

public sealed record PersonalDataExportWorkItem(
    Guid ExportId,
    Guid UserId,
    string FileName,
    DateTimeOffset ExpiresAt);

public interface IPersonalDataExportStore
{
    Task<QueueDataExportResult> QueuePersonalAsync(
        QueuePersonalDataExportRequest request,
        CancellationToken cancellationToken);

    Task<PersonalDataExportWorkItem?> StartPersonalAsync(
        Guid exportId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<DataExportReadSnapshot?> FindOwnedPersonalAsync(
        Guid userId,
        Guid exportId,
        CancellationToken cancellationToken);
}

public sealed record PersonalDataArchive(
    byte[] Content,
    int RecordCount,
    IReadOnlyCollection<string> Parts);

public interface IPersonalDataArchiveBuilder
{
    Task<PersonalDataArchive> BuildAsync(
        Guid userId,
        DateTimeOffset generatedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
}

public static class PersonalDataExportJob
{
    public const string Type = "personal-data-export";
}

public sealed record PersonalDataExportJobPayload(Guid ExportId);
