using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateBudgetCommand : BaseCommand
{
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public BudgetPeriodType PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public IReadOnlyCollection<Guid> CategoryIds { get; set; } = [];
    public bool IncludeDescendants { get; set; } = true;
}

public sealed class UpdateBudgetCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public BudgetPeriodType PeriodType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public IReadOnlyCollection<Guid> CategoryIds { get; set; } = [];
    public bool IncludeDescendants { get; set; } = true;
}

public sealed class DeleteBudgetCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
