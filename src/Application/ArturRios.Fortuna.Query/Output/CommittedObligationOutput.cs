using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class CommittedObligationListOutput : QueryOutput
{
    public DateOnly AsOf { get; set; }
    public DateOnly Through { get; set; }
    public string DisplayCurrencyCode { get; set; } = string.Empty;
    public decimal? Total { get; set; }
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<CommittedObligationOutput> Items { get; set; } = [];
    public IReadOnlyCollection<CommittedObligationPeriodOutput> Periods { get; set; } = [];
    public IReadOnlyCollection<CommittedObligationRateOutput> Rates { get; set; } = [];
}

public sealed class CommittedObligationOutput
{
    public Guid Id { get; set; }
    public CommittedObligationKind Kind { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? CycleStart { get; set; }
    public DateOnly? CycleEnd { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? DisplayAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}

public sealed class CommittedObligationPeriodOutput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal? Total { get; set; }
    public bool IsFullyConverted { get; set; }
}

public sealed class CommittedObligationRateOutput
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
    public ExchangeRateSource Source { get; set; }
}
