using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordTransactionCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? CreditCardId { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public TransactionDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? Description { get; set; }
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public IReadOnlyCollection<TransactionTagOutput> Tags { get; set; } = [];
    public Guid? StatementId { get; set; }
    public DateOnly? StatementPeriodStart { get; set; }
    public DateOnly? StatementPeriodEnd { get; set; }
    public DateOnly? StatementClosingDate { get; set; }
    public DateOnly? StatementDueDate { get; set; }
    public string? StatementStatus { get; set; }
    public decimal? StatementPurchaseTotal { get; set; }
    public bool IsLateArriving { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionTagOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
