using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class AttachmentOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
