using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordTransferCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid OutboundTransactionId { get; set; }
    public Guid InboundTransactionId { get; set; }
    public Guid OriginFinancialAccountId { get; set; }
    public Guid? DestinationFinancialAccountId { get; set; }
    public Guid? DestinationStatementId { get; set; }
    public decimal OutboundAmount { get; set; }
    public string OutboundCurrencyCode { get; set; } = string.Empty;
    public decimal InboundAmount { get; set; }
    public string InboundCurrencyCode { get; set; } = string.Empty;
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
