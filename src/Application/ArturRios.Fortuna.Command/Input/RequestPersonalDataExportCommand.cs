using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RequestPersonalDataExportCommand : BaseCommand
{
    [JsonIgnore]
    public string? CorrelationId { get; set; }
}
