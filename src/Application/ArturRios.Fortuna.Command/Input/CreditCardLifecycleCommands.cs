using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class DeleteCreditCardCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class RestoreCreditCardCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}

public sealed class HardDeleteCreditCardCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
