namespace ArturRios.Fortuna.Shared.Transactions;

public interface ITransferStore
{
    Task<TransferRecordResult> RecordAsync(
        TransferRecord record,
        CancellationToken cancellationToken);
}

public interface ITransferReader
{
    Task<TransferReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

public interface ITransferLifecycleStore
{
    Task<TransferLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<TransferLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public sealed record TransferRecord(
    Guid UserId,
    Guid OriginFinancialAccountId,
    Guid DestinationFinancialAccountId,
    decimal Amount,
    DateOnly OccurredOn,
    DateTimeOffset CreatedAt);

public enum TransferRecordOutcome
{
    Succeeded = 1,
    OriginFinancialAccountNotFound = 2,
    DestinationFinancialAccountNotFound = 3,
    AccountsMustDiffer = 4,
    ExchangeRateUnavailable = 5,
    ConvertedAmountTooSmall = 6
}

public sealed record TransferRecordResult(
    TransferSnapshot? Transfer,
    TransferRecordOutcome Outcome);

public sealed record TransferSnapshot(
    Guid Id,
    Guid OutboundTransactionId,
    Guid InboundTransactionId,
    Guid OriginFinancialAccountId,
    Guid DestinationFinancialAccountId,
    decimal OutboundAmount,
    string OutboundCurrencyCode,
    decimal InboundAmount,
    string InboundCurrencyCode,
    decimal? AppliedRate,
    DateOnly? RateDate,
    DateOnly OccurredOn,
    DateTimeOffset CreatedAt);

public sealed class TransferReadSnapshot
{
    public Guid Id { get; init; }
    public Guid OutboundTransactionId { get; init; }
    public Guid? InboundTransactionId { get; init; }
    public Guid? InboundInvestmentMovementId { get; init; }
    public Guid OriginFinancialAccountId { get; init; }
    public Guid? DestinationFinancialAccountId { get; init; }
    public Guid? DestinationCreditCardId { get; init; }
    public Guid? DestinationStatementId { get; init; }
    public Guid? DestinationInvestmentId { get; init; }
    public decimal OutboundAmount { get; init; }
    public string OutboundCurrencyCode { get; init; } = string.Empty;
    public decimal InboundAmount { get; init; }
    public string InboundCurrencyCode { get; init; } = string.Empty;
    public decimal? AppliedRate { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateOnly OccurredOn { get; init; }
    public bool OutboundIsDeleted { get; init; }
    public bool InboundIsDeleted { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum TransferLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    RestoreRequiresSoftDeletion = 3,
    SettledStatementFrozen = 4
}

public sealed record TransferLifecycleResult(
    Guid? Id,
    TransferLifecycleOutcome Outcome);
