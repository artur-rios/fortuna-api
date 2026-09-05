using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteInstallmentPlanCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class RestoreInstallmentPlanCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
