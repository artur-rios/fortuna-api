using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordInstallmentPlanCommand : BaseCommand
{
    public Guid CreditCardId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal TotalAmount { get; set; }
    public short InstallmentCount { get; set; }
    public DateOnly PurchasedOn { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Counterparty { get; set; }
    public Guid? OwnerId { get; set; }
}
