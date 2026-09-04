using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class ConvertFigureQueryOutput : QueryOutput
{
    public string DisplayCurrencyCode { get; set; } = string.Empty;
    public DateOnly FigureDate { get; set; }
    public decimal? Total { get; set; }
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<ConvertedCurrencyGroupOutput> Groups { get; set; } = [];
}

public sealed class ConvertedCurrencyGroupOutput
{
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public decimal? DisplayAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
