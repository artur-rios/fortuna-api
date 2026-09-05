using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class TransactionLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
}
