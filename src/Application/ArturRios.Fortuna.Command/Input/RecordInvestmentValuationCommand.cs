using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordInvestmentValuationCommand : BaseCommand
{
    public Guid Id { get; set; }
    public decimal Value { get; set; }
    public DateOnly ValuedOn { get; set; }
}
