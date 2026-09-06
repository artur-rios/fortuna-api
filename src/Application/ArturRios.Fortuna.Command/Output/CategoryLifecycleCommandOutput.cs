using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CategoryLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public int LiveTransactionCount { get; set; }
}
