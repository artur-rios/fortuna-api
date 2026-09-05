using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteInvestmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class RestoreInvestmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class HardDeleteInvestmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
