using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class CreditCardStatementOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid CreditCardId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly ClosingDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal PaymentsReceived { get; set; }
    public decimal PurchaseTotal { get; set; }
    public decimal ForeignTaxTotal { get; set; }
    public decimal OtherEntries { get; set; }
    public decimal AmountDue { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? SettlementTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CreditCardStatementTransactionOutput> Transactions { get; set; } = [];
}

public sealed class CreditCardStatementTransactionOutput
{
    public Guid Id { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
    public bool IsLateArriving { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
