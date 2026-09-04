using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CreditCardLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal OutstandingAmount { get; set; }
}
