using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteAttachmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class HardDeleteAttachmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
