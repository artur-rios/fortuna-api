using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class RetrieveDataExportQueryOutput : QueryOutput
{
    public Guid Id { get; init; }
    public Guid? JobId { get; init; }
    public DataExportFormat Format { get; init; }
    public DataExportStatus Status { get; init; }
    public string FileName { get; init; } = string.Empty;
    public int? RowCount { get; init; }
    public string? ContentType { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonIgnore]
    public Stream? Content { get; init; }
}
