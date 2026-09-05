using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteTransactionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class RestoreTransactionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class HardDeleteTransactionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
