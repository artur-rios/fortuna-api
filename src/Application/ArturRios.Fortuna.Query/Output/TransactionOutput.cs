using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TransactionOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public string? FinancialAccountName { get; set; }
    public Guid? CreditCardId { get; set; }
    public string? CreditCardName { get; set; }
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public Guid? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public TransactionDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? Description { get; set; }
    public TransactionSourceType SourceType { get; set; }
    public bool IsReconciled { get; set; }
    public bool IsManuallyCorrected { get; set; }
    public bool IsTransfer { get; set; }
    public Guid? InstallmentPlanId { get; set; }
    public short? InstallmentNumber { get; set; }
    public Guid? RecurringTransactionId { get; set; }
    public Guid? ImportJobId { get; set; }
    public long? ImportedRecordId { get; set; }
    public decimal? ImportedAmount { get; set; }
    public DateOnly? ImportedOccurredOn { get; set; }
    public Guid? StatementId { get; set; }
    public bool IsLateArriving { get; set; }
    public bool IsPossibleDuplicate { get; set; }
    public IReadOnlyCollection<TransactionLabelOutput> Tags { get; set; } = [];
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionLabelOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
