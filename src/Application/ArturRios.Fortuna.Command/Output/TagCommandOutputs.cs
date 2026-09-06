using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class TagCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public int DetachedTransactionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionTagCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid TagId { get; set; }
    public bool IsAttached { get; set; }
    public int TagCount { get; set; }
}
