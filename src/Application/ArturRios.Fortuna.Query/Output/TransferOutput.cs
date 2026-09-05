using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TransferOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid OutboundTransactionId { get; set; }
    public Guid? InboundTransactionId { get; set; }
    public Guid? InboundInvestmentMovementId { get; set; }
    public Guid OriginFinancialAccountId { get; set; }
    public Guid? DestinationFinancialAccountId { get; set; }
    public Guid? DestinationCreditCardId { get; set; }
    public Guid? DestinationStatementId { get; set; }
    public Guid? DestinationInvestmentId { get; set; }
    public decimal OutboundAmount { get; set; }
    public string OutboundCurrencyCode { get; set; } = string.Empty;
    public decimal InboundAmount { get; set; }
    public string InboundCurrencyCode { get; set; } = string.Empty;
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly OccurredOn { get; set; }
    public bool OutboundIsDeleted { get; set; }
    public bool InboundIsDeleted { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
