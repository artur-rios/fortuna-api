using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class TransferLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
}
