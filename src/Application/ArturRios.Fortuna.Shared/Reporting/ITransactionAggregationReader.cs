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
    string? Text,
    IReadOnlyCollection<TransactionAggregationSelection> Selections);

public sealed record TransactionAggregationSelection(
    string Dimension,
    string Value,
    bool RollupCategories,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record TransactionAggregationFigureSnapshot(
    string DimensionValue,
    string Label,
    DateOnly? BucketStart,
    string CurrencyCode,
    DateOnly FigureDate,
    decimal Amount,
    int RecordCount);

public sealed record TransactionDrillDownOptions(TimeSpan KeyLifetime);

public interface ITransactionDrillDownKeyCodec
{
    string Encode(TransactionDrillDownKeyPayload payload);

    bool TryDecode(string key, out TransactionDrillDownKeyPayload? payload);
}

public sealed record TransactionDrillDownKeyPayload(
    int Version,
    Guid OwnerId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Dimension,
    string? Granularity,
    string DisplayCurrencyCode,
    DateOnly From,
    DateOnly To,
    IReadOnlyCollection<TransactionAggregationSelection> Selections,
    int RecordCount,
    TransactionDrillDownFilters Filters);

public sealed record TransactionDrillDownFilters(
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid? CategoryId,
    Guid? TagId,
    Guid? CounterpartyId,
    TransactionDirection? Direction,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string? Text);
