using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Transactions;

public interface ITransactionStore
{
    Task<TransactionRecordResult> RecordAsync(
        TransactionRecord record,
        CancellationToken cancellationToken);
}

public interface ITransactionReader
{
    IQueryable<TransactionReadSnapshot> Query(TransactionSearchCriteria criteria);

    Task<TransactionReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TransactionCurrencyTotalSnapshot>> SummarizeAsync(
        TransactionSearchCriteria criteria,
        CancellationToken cancellationToken);
}

public interface ITransactionUpdater
{
    Task<TransactionUpdateResult> UpdateAsync(
        TransactionUpdate update,
        CancellationToken cancellationToken);
}

public interface ITransactionLifecycleStore
{
    Task<TransactionLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<TransactionLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<TransactionLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public interface ITransactionReconciliationStore
{
    Task<TransactionReconciliationResult> ReconcileAsync(
        TransactionReconciliation change,
        CancellationToken cancellationToken);
}

public sealed class TransactionSearchCriteria
{
    public Guid UserId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public Guid? CreditCardId { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? TagId { get; init; }
    public Guid? CounterpartyId { get; init; }
    public TransactionDirection? Direction { get; init; }
    public decimal? MinimumAmount { get; init; }
    public decimal? MaximumAmount { get; init; }
    public string? Text { get; init; }
    public bool IncludeDeleted { get; init; }
}

public sealed class TransactionReadSnapshot
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public string? FinancialAccountName { get; init; }
    public Guid? CreditCardId { get; init; }
    public string? CreditCardName { get; init; }
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public Guid? CounterpartyId { get; init; }
    public string? CounterpartyName { get; init; }
    public TransactionDirection Direction { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal? OriginalAmount { get; init; }
    public string? OriginalCurrencyCode { get; init; }
    public decimal? AppliedRate { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateOnly OccurredOn { get; init; }
    public string? Description { get; init; }
    public TransactionSourceType SourceType { get; init; }
    public bool IsReconciled { get; init; }
    public bool IsManuallyCorrected { get; init; }
    public bool IsTransfer { get; init; }
    public Guid? InstallmentPlanId { get; init; }
    public short? InstallmentNumber { get; init; }
    public Guid? RecurringTransactionId { get; init; }
    public Guid? ImportJobId { get; init; }
    public long? ImportedRecordId { get; init; }
    public decimal? ImportedAmount { get; init; }
    public DateOnly? ImportedOccurredOn { get; init; }
    public Guid? StatementId { get; init; }
    public bool IsLateArriving { get; init; }
    public bool IsPossibleDuplicate { get; init; }
    public IReadOnlyCollection<TransactionReadTagSnapshot> Tags { get; init; } = [];
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TransactionReadTagSnapshot(Guid Id, string Name);

public sealed record TransactionCurrencyTotalSnapshot(
    string CurrencyCode,
    decimal Expense,
    decimal Earning);

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

public sealed record TransactionUpdate(
    Guid UserId,
    Guid Id,
    Guid CategoryId,
    TransactionDirection Direction,
    decimal Amount,
    DateOnly OccurredOn,
    string? Description,
    string? Counterparty,
    IReadOnlyCollection<string> Tags,
    DateTimeOffset UpdatedAt);

public enum TransactionUpdateOutcome
{
    Succeeded = 1,
    NotFound = 2,
    CategoryNotFound = 3,
    SettledStatementFrozen = 4,
    TransferFieldsRestricted = 5,
    InstallmentFieldsRestricted = 6
}

public sealed record TransactionUpdateResult(
    TransactionReadSnapshot? Transaction,
    TransactionUpdateOutcome Outcome);

public enum TransactionLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    RestoreRequiresSoftDeletion = 3,
    HardDeleteRequiresSoftDeletion = 4,
    SettledStatementFrozen = 5
}

public sealed record TransactionLifecycleResult(
    Guid? Id,
    TransactionLifecycleOutcome Outcome);

public sealed record TransactionReconciliation(
    Guid UserId,
    Guid TransactionId,
    Guid? ImportJobId,
    long? ImportedRecordId,
    bool Unreconcile,
    DateTimeOffset ChangedAt);

public enum TransactionReconciliationOutcome
{
    Succeeded = 1,
    TransactionNotFound = 2,
    ImportedRecordNotFound = 3,
    TransactionAlreadyReconciled = 4,
    TransactionNotReconciled = 5,
    ImportedRecordAlreadyMatched = 6,
    SettledStatementFrozen = 7
}

public sealed record TransactionReconciliationResult(
    TransactionReadSnapshot? Transaction,
    TransactionReconciliationOutcome Outcome,
    Guid? ConflictingTransactionId = null);

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
