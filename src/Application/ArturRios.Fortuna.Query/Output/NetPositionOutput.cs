using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class NetPositionOutput : QueryOutput
{
    public DateOnly AsOf { get; set; }
    public string DisplayCurrencyCode { get; set; } = string.Empty;
    public decimal? Total { get; set; }
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<NetPositionCurrencyOutput> CurrencyGroups { get; set; } = [];
}

public sealed class NetPositionCurrencyOutput
{
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal FinancialAccounts { get; set; }
    public decimal Investments { get; set; }
    public decimal CreditCards { get; set; }
    public decimal SourceNet { get; set; }
    public decimal? DisplayNet { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
