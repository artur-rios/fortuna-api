using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class PersonalDataExportQueryOutput : QueryOutput
{
    public Guid JobId { get; init; }
    public DataExportStatus Status { get; init; }
    public int Progress { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonIgnore]
    public Stream? Content { get; init; }
}
