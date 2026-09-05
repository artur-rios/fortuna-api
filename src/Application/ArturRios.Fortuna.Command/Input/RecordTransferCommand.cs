using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordTransferCommand : BaseCommand
{
    public Guid OriginFinancialAccountId { get; set; }
    public Guid? DestinationFinancialAccountId { get; set; }
    public Guid? DestinationStatementId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; }
    public Guid? OwnerId { get; set; }
}
