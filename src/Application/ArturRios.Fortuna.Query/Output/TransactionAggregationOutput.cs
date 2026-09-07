using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TransactionAggregationOutput : QueryOutput
{
    public string Dimension { get; set; } = string.Empty;
    public string? Granularity { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string DisplayCurrencyCode { get; set; } = string.Empty;
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<TransactionAggregationBucketOutput> Buckets { get; set; } = [];
}

public sealed class TransactionAggregationBucketOutput
{
    public string Label { get; set; } = string.Empty;
    public decimal? Total { get; set; }
    public decimal? Share { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public string DrillDownKey { get; set; } = string.Empty;
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<TransactionAggregationConversionOutput> Conversions { get; set; } = [];
}

public sealed class TransactionAggregationConversionOutput
{
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public DateOnly FigureDate { get; set; }
    public decimal? DisplayAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
