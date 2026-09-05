using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Transactions;

public interface ITransactionStore
{
    Task<TransactionRecordResult> RecordAsync(
        TransactionRecord record,
        CancellationToken cancellationToken);
}

public sealed record TransactionRecord(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid CategoryId,
    TransactionDirection Direction,
    decimal Amount,
    string? CurrencyCode,
    DateOnly OccurredOn,
    string? Description,
    string? Counterparty,
    IReadOnlyCollection<string> Tags,
    DateTimeOffset CreatedAt);

public enum TransactionRecordOutcome
{
    Succeeded = 1,
    FinancialAccountNotFound = 2,
    CreditCardNotFound = 3,
    CategoryNotFound = 4,
    CurrencyNotSupported = 5,
    ExchangeRateUnavailable = 6,
    ConvertedAmountTooSmall = 7
}

public sealed record TransactionRecordResult(
    TransactionSnapshot? Transaction,
    TransactionRecordOutcome Outcome);

public sealed record TransactionSnapshot(
    Guid Id,
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid CategoryId,
    string CategoryName,
    TransactionDirection Direction,
    decimal Amount,
    string CurrencyCode,
    decimal? OriginalAmount,
    string? OriginalCurrencyCode,
    decimal? AppliedRate,
    DateOnly? RateDate,
    DateOnly OccurredOn,
    string? Description,
    Guid? CounterpartyId,
    string? CounterpartyName,
    IReadOnlyCollection<TransactionTagSnapshot> Tags,
    Guid? StatementId,
    DateOnly? StatementPeriodStart,
    DateOnly? StatementPeriodEnd,
    DateOnly? StatementClosingDate,
    DateOnly? StatementDueDate,
    string? StatementStatus,
    decimal? StatementPurchaseTotal,
    bool IsLateArriving,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TransactionTagSnapshot(Guid Id, string Name);
