using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class ReassignCategoryTransactionsCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Guid TargetCategoryId { get; set; }
    public bool IncludeDescendants { get; set; }
}
