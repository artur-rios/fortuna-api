using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CloseCreditCardStatementCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
