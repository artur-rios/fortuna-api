using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordCardChargeCommand : BaseCommand
{
    public Guid CreditCardId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
}
