using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateTagCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateTagCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class DeleteTagCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class AttachTransactionTagCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public Guid TagId { get; set; }
}

public sealed class DetachTransactionTagCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public Guid TagId { get; set; }
}
