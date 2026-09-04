using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CloseCreditCardStatementCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid CreditCardId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly ClosingDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal PurchaseTotal { get; set; }
    public decimal AmountDue { get; set; }
}
