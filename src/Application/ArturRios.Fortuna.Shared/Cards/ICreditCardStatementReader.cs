using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Cards;

public interface ICreditCardStatementReader
{
    IQueryable<CreditCardStatementReadSnapshot> Query(Guid userId);

    Task<CreditCardStatementReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken);
}

public sealed class CreditCardStatementReadSnapshot
{
    public Guid Id { get; init; }
    public Guid CreditCardId { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public DateOnly ClosingDate { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal PreviousBalance { get; init; }
    public decimal PaymentsReceived { get; init; }
    public decimal PurchaseTotal { get; init; }
    public decimal ForeignTaxTotal { get; init; }
    public decimal OtherEntries { get; init; }
    public decimal AmountDue { get; init; }
    public CreditCardStatementStatus Status { get; init; }
    public Guid? SettlementTransactionId { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public List<CreditCardStatementTransactionSnapshot> Transactions { get; init; } = [];
}

public sealed class CreditCardStatementTransactionSnapshot
{
    public Guid Id { get; init; }
    public TransactionDirection Direction { get; init; }
    public decimal Amount { get; init; }
    public DateOnly OccurredOn { get; init; }
    public bool IsLateArriving { get; init; }
    public decimal? OriginalAmount { get; init; }
    public string? OriginalCurrencyCode { get; init; }
    public decimal? AppliedRate { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
