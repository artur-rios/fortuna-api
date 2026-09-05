using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class InvestmentOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public InvestmentType InvestmentType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Position { get; set; }
    public bool IsIndependentlyValued { get; set; }
    public decimal? LatestValuationValue { get; set; }
    public DateOnly? LatestValuationDate { get; set; }
    public string? DisplayCurrencyCode { get; set; }
    public decimal? DisplayPosition { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
