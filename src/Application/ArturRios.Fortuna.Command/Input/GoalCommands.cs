using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateGoalCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; }
    public IReadOnlyCollection<Guid> AccountIds { get; set; } = [];
    public IReadOnlyCollection<Guid> InvestmentIds { get; set; } = [];
}

public sealed class UpdateGoalCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly TargetDate { get; set; }
    public IReadOnlyCollection<Guid> AccountIds { get; set; } = [];
    public IReadOnlyCollection<Guid> InvestmentIds { get; set; } = [];
}

public sealed class DeleteGoalCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
