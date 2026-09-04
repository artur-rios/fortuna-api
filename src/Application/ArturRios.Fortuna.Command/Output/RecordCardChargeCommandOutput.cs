using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordCardChargeCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid CreditCardId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
    public bool IsLateArriving { get; set; }
    public Guid StatementId { get; set; }
    public DateOnly StatementPeriodStart { get; set; }
    public DateOnly StatementPeriodEnd { get; set; }
    public DateOnly StatementClosingDate { get; set; }
    public DateOnly StatementDueDate { get; set; }
    public string StatementStatus { get; set; } = string.Empty;
    public decimal StatementPurchaseTotal { get; set; }
}
