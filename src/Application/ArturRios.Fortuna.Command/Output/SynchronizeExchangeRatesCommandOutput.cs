using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class SynchronizeExchangeRatesCommandOutput : CommandOutput
{
    public Guid JobId { get; set; }
    public DateOnly RequestedDate { get; set; }
}
