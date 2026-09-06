using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class BudgetCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public BudgetPeriodType PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public bool IncludeDescendants { get; set; }
    public IReadOnlyCollection<BudgetCategoryCommandOutput> Categories { get; set; } = [];
    public BudgetConsumptionCommandOutput CurrentPeriod { get; set; } = new();
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BudgetCategoryCommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class BudgetConsumptionCommandOutput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal? Spent { get; set; }
    public decimal? Remaining { get; set; }
    public bool? IsExceeded { get; set; }
    public decimal? Overage { get; set; }
    public bool IsFullyConverted { get; set; }
}
