using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class SettleCreditCardStatementCommand : BaseCommand
{
    public Guid Id { get; set; }
    public Guid FinancialAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
}
