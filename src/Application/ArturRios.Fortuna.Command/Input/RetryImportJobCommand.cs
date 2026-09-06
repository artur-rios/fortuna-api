using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RetryImportJobCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
