using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class GoalCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; }
    public IReadOnlyCollection<GoalResourceCommandOutput> Accounts { get; set; } = [];
    public IReadOnlyCollection<GoalResourceCommandOutput> Investments { get; set; } = [];
    public GoalProgressCommandOutput CurrentProgress { get; set; } = new();
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GoalResourceCommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class GoalProgressCommandOutput
{
    public decimal? CurrentAmount { get; set; }
    public decimal? Remaining { get; set; }
    public decimal? ProportionReached { get; set; }
    public bool? IsReached { get; set; }
    public bool IsFullyConverted { get; set; }
}
