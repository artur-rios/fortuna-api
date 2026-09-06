using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class GoalOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; }
    public IReadOnlyCollection<GoalResourceOutput> Accounts { get; set; } = [];
    public IReadOnlyCollection<GoalResourceOutput> Investments { get; set; } = [];
    public GoalProgressOutput CurrentProgress { get; set; } = new();
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GoalResourceOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class GoalProgressOutput
{
    public decimal? CurrentAmount { get; set; }
    public decimal? Remaining { get; set; }
    public decimal? ProportionReached { get; set; }
    public bool? IsReached { get; set; }
    public bool IsFullyConverted { get; set; }
}

public sealed class GoalListOutput : QueryOutput
{
    public IReadOnlyCollection<GoalOutput> Goals { get; set; } = [];
}

public sealed class GoalProgressDetailOutput : QueryOutput
{
    public Guid GoalId { get; set; }
    public decimal TargetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; }
    public DateOnly AsOf { get; set; }
    public decimal? CurrentAmount { get; set; }
    public decimal? Shortfall { get; set; }
    public decimal? ProportionReached { get; set; }
    public bool? IsReached { get; set; }
    public int DaysRemaining { get; set; }
    public bool? IsPastDue { get; set; }
    public bool IsFullyConverted { get; set; }
    public IReadOnlyCollection<GoalResourceProgressOutput> Resources { get; set; } = [];
}

public sealed class GoalResourceProgressOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Shared.Planning.GoalResourceType ResourceType { get; set; }
    public string SourceCurrencyCode { get; set; } = string.Empty;
    public decimal? SourceAmount { get; set; }
    public decimal? ConvertedAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public Domain.Currencies.ExchangeRateSource? RateSource { get; set; }
    public bool IsIncluded { get; set; }
    public string? ExclusionReason { get; set; }
    public string? UnconvertedReason { get; set; }
}
