using System.Text.Json;
using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class ReauthenticateConnectionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string ExternalReference { get; set; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalFields { get; set; }
}
