using System.Text.Json.Serialization;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class DownloadAttachmentQueryOutput : QueryOutput
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }

    [JsonIgnore]
    public Stream Content { get; init; } = Stream.Null;
}
