using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecurringTransactionLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public bool MaterializedOccurrencesChanged { get; set; }
}
