using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordInvestmentMovementCommand : BaseCommand
{
    public Guid Id { get; set; }
    public InvestmentMovementType MovementType { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
    public Guid? FinancialAccountId { get; set; }
}
