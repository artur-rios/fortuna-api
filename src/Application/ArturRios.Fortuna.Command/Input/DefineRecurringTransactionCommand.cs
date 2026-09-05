using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DefineRecurringTransactionCommand : BaseCommand
{
    public Guid? FinancialAccountId { get; set; }
    public Guid? CreditCardId { get; set; }
    public Guid CategoryId { get; set; }
    public TransactionDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public string? Description { get; set; }
    public string? Counterparty { get; set; }
    public Guid? OwnerId { get; set; }
}
