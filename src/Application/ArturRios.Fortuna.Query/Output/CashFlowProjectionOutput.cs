using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public enum CashFlowFigureKind
{
    Recorded = 1,
    Projected = 2,
    Committed = 3,
    Estimated = 4
}

public sealed class CashFlowProjectionOutput : QueryOutput
{
    public DateOnly AsOf { get; set; }
    public DateOnly Through { get; set; }
    public string DisplayCurrencyCode { get; set; } = string.Empty;
    public CashFlowPeriodicity Periodicity { get; set; }
    public decimal StartingBalance { get; set; }
    public string? FlatReason { get; set; }
    public string? EstimateOmittedReason { get; set; }
    public IReadOnlyCollection<CashFlowPeriodOutput> Periods { get; set; } = [];
    public IReadOnlyCollection<CashFlowRateOutput> Rates { get; set; } = [];
}

public sealed class CashFlowPeriodOutput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyCollection<CashFlowFigureOutput> Figures { get; set; } = [];
}

public sealed class CashFlowFigureOutput
{
    public CashFlowFigureKind Kind { get; set; }
    public decimal Amount { get; set; }
}

public sealed class CashFlowRateOutput
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
    public ExchangeRateSource Source { get; set; }
}
