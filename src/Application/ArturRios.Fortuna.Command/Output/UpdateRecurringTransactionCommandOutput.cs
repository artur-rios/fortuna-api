using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class UpdateRecurringTransactionCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? CreditCardId { get; set; }
    public Guid CategoryId { get; set; }
    public TransactionDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public RecurrenceFrequency Frequency { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public DateOnly? LastMaterializedOn { get; set; }
    public string? Description { get; set; }
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public DateOnly? AppliesFrom { get; set; }
    public bool MaterializedOccurrencesChanged { get; set; }
    public IReadOnlyCollection<DateOnly> NextOccurrences { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
