using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class SynchronizeConnectionCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public string? CorrelationId { get; set; }

    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}
