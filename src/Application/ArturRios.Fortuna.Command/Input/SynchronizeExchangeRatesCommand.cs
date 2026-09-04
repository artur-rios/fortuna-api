using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class SynchronizeExchangeRatesCommand : BaseCommand
{
    public DateOnly? RequestedDate { get; set; }
    public string? CorrelationId { get; set; }
}
