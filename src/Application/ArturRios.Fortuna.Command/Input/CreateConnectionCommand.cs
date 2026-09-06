using System.Text.Json;
using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateConnectionCommand : BaseCommand
{
    public string DataSource { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; set; }
}
