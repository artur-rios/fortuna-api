namespace ArturRios.Fortuna.Shared.Transactions;

public interface ITransferStore
{
    Task<TransferRecordResult> RecordAsync(
        TransferRecord record,
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
