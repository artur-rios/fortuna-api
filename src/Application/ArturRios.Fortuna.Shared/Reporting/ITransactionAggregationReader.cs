using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Reporting;

public interface ITransactionAggregationReader
{
    Task<IReadOnlyCollection<TransactionAggregationFigureSnapshot>> ReadAsync(
        TransactionAggregationCriteria criteria,
        CancellationToken cancellationToken);
}

public sealed record TransactionAggregationOptions(int MaximumSpanDays);

public sealed record TransactionAggregationCriteria(
    Guid UserId,
    string Dimension,
    string? Granularity,
    DateOnly From,
    DateOnly To,
    bool RollupCategories,
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid? CategoryId,
    Guid? TagId,
    Guid? CounterpartyId,
    TransactionDirection? Direction,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string? Text);

public sealed record TransactionAggregationFigureSnapshot(
    string DimensionValue,
    string Label,
    DateOnly? BucketStart,
    string CurrencyCode,
    DateOnly FigureDate,
    decimal Amount);
