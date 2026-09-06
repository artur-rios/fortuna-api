using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CounterpartyCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public bool Reused { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CounterpartyMergeCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public int ReassignedTransactionCount { get; set; }
}
