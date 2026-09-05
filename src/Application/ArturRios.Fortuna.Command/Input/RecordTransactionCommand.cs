using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordTransactionCommand : BaseCommand
{
    public DateOnly OccurredOn { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? CreditCardId { get; set; }
    public Guid CategoryId { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Description { get; set; }
    public string? Counterparty { get; set; }
    public IReadOnlyCollection<string>? Tags { get; set; }
    public Guid? OwnerId { get; set; }
}
