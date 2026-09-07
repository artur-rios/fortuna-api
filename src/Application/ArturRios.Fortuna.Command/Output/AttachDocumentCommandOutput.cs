using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class AttachDocumentCommandOutput : CommandOutput
{
    public Guid Id { get; init; }
    public Guid TransactionId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
