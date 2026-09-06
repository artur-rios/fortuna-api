using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateCounterpartyCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateCounterpartyCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class DeleteCounterpartyCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class MergeCounterpartiesCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public Guid TargetId { get; set; }
}
