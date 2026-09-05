using ArturRios.Fortuna.Domain.Investments;

namespace ArturRios.Fortuna.Shared.Investments;

public interface IInvestmentValuationStore
{
    Task<InvestmentValuationRecordResult> RecordAsync(
        InvestmentValuationRecord record,
        CancellationToken cancellationToken);
}

public sealed record InvestmentValuationRecord(
    Guid UserId,
    Guid InvestmentId,
    decimal Value,
    DateOnly ValuedOn,
    DateTimeOffset RecordedAt);

public enum InvestmentValuationRecordOutcome
{
    Succeeded = 1,
    InvestmentNotFound = 2
}

public sealed record InvestmentValuationRecordResult(
    InvestmentValuationSnapshot? Valuation,
    InvestmentValuationRecordOutcome Outcome);

public sealed record InvestmentValuationSnapshot(
    Guid Id,
    Guid InvestmentId,
    decimal Value,
    string CurrencyCode,
    DateOnly ValuedOn,
    bool ReplacedExisting,
    decimal Position,
    bool IsIndependentlyValued,
    decimal? LatestValuationValue,
    DateOnly? LatestValuationDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
