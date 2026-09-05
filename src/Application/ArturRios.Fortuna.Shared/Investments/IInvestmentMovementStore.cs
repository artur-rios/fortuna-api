using ArturRios.Fortuna.Domain.Investments;

namespace ArturRios.Fortuna.Shared.Investments;

public interface IInvestmentMovementStore
{
    Task<InvestmentMovementRecordResult> RecordAsync(
        InvestmentMovementRecord record,
        CancellationToken cancellationToken);
}

public sealed record InvestmentMovementRecord(
    Guid UserId,
    Guid InvestmentId,
    InvestmentMovementType MovementType,
    decimal Amount,
    DateOnly OccurredOn,
    Guid? FinancialAccountId,
    DateTimeOffset CreatedAt);

public enum InvestmentMovementRecordOutcome
{
    Succeeded = 1,
    InvestmentNotFound = 2,
    FinancialAccountNotFound = 3,
    ExchangeRateUnavailable = 4,
    ConvertedAmountTooSmall = 5
}

public sealed record InvestmentMovementRecordResult(
    InvestmentMovementSnapshot? Movement,
    InvestmentMovementRecordOutcome Outcome);

public sealed record InvestmentMovementSnapshot(
    Guid Id,
    Guid InvestmentId,
    InvestmentMovementType MovementType,
    decimal Amount,
    string CurrencyCode,
    DateOnly OccurredOn,
    decimal Position,
    Guid? FinancialAccountId,
    decimal? FundingAmount,
    string? FundingCurrencyCode,
    Guid? TransferId,
    Guid? OutboundTransactionId,
    decimal? AppliedRate,
    DateOnly? RateDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
