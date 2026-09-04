using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteFinancialAccountCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class RestoreFinancialAccountCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class HardDeleteFinancialAccountCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
