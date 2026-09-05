namespace ArturRios.Fortuna.Shared.Transactions;

public interface IInstallmentPlanStore
{
    Task<InstallmentPlanRecordResult> RecordAsync(
        InstallmentPlanRecord record,
        CancellationToken cancellationToken);
}

public interface IInstallmentPlanReader
{
    Task<InstallmentPlanSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

public interface IInstallmentPlanLifecycleStore
{
    Task<InstallmentPlanLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<InstallmentPlanLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public sealed record InstallmentPlanRecord(
    Guid UserId,
    Guid CreditCardId,
    Guid CategoryId,
    decimal TotalAmount,
    short InstallmentCount,
    DateOnly PurchasedOn,
    string? CurrencyCode,
    string? Counterparty,
    DateTimeOffset CreatedAt);

public enum InstallmentPlanRecordOutcome
{
    Succeeded = 1,
    CreditCardNotFound = 2,
    CategoryNotFound = 3,
    CurrencyNotSupported = 4,
    ExchangeRateUnavailable = 5,
    AmountTooSmall = 6
}

public sealed record InstallmentPlanRecordResult(
    InstallmentPlanSnapshot? Plan,
    InstallmentPlanRecordOutcome Outcome);

public sealed class InstallmentPlanSnapshot
{
    public Guid Id { get; init; }
    public Guid CreditCardId { get; init; }
    public decimal TotalAmount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal? OriginalTotalAmount { get; init; }
    public string? OriginalCurrencyCode { get; init; }
    public decimal? AppliedRate { get; init; }
    public DateOnly? RateDate { get; init; }
    public short InstallmentCount { get; init; }
    public DateOnly PurchasedOn { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyCollection<InstallmentSnapshot> Installments { get; init; } = [];
}

public sealed record InstallmentSnapshot(
    Guid TransactionId,
    short Number,
    decimal Amount,
    string CurrencyCode,
    decimal? OriginalAmount,
    string? OriginalCurrencyCode,
    decimal? AppliedRate,
    DateOnly? RateDate,
    DateOnly OccurredOn,
    Guid? StatementId,
    bool IsLateArriving,
    bool IsDeleted);

public enum InstallmentPlanLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    RestoreRequiresSoftDeletion = 3,
    SettledStatementFrozen = 4
}

public sealed record InstallmentPlanLifecycleResult(
    Guid? Id,
    InstallmentPlanLifecycleOutcome Outcome);
