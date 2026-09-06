using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class BudgetOutput : QueryOutput
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public BudgetPeriodType PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public bool IncludeDescendants { get; set; }
    public IReadOnlyCollection<BudgetCategoryOutput> Categories { get; set; } = [];
    public BudgetConsumptionOutput CurrentPeriod { get; set; } = new();
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BudgetCategoryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class BudgetConsumptionOutput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal? Spent { get; set; }
    public decimal? Remaining { get; set; }
    public bool? IsExceeded { get; set; }
    public decimal? Overage { get; set; }
    public bool IsFullyConverted { get; set; }
}

public sealed class BudgetListOutput : QueryOutput
{
    public IReadOnlyCollection<BudgetOutput> Budgets { get; set; } = [];
}

public sealed class BudgetConsumptionDetailOutput : QueryOutput
{
    public Guid BudgetId { get; set; }
    public decimal BudgetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly RequestedDate { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public decimal? Spent { get; set; }
    public decimal? Remaining { get; set; }
    public bool? IsExceeded { get; set; }
    public decimal? Overage { get; set; }
    public bool IsCovered { get; set; }
    public bool IsFullyConverted { get; set; }
    public string? Reason { get; set; }
    public IReadOnlyCollection<BudgetConversionOutput> Conversions { get; set; } = [];
}

public sealed class BudgetConversionOutput
{
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal SourceAmount { get; set; }
    public decimal? ConvertedAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
