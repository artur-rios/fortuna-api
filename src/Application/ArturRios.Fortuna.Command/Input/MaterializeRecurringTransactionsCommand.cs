using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class MaterializeRecurringTransactionsCommand : BaseCommand
{
    public Guid? OwnerId { get; set; }
}
