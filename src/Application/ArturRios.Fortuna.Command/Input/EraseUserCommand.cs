using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class EraseUserCommand : BaseCommand
{
    public string? Confirmation { get; set; }

    [JsonIgnore]
    public Guid? UserId { get; set; }

    [JsonIgnore]
    public bool IsSelfService { get; set; }
}
