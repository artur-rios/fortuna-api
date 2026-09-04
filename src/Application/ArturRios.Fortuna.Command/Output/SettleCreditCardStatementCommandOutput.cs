using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class SettleCreditCardStatementCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid TransferId { get; set; }
    public Guid OutboundTransactionId { get; set; }
    public Guid InboundTransactionId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public decimal PaymentAmount { get; set; }
    public string PaymentCurrencyCode { get; set; } = string.Empty;
    public decimal AppliedAmount { get; set; }
    public string CreditCardCurrencyCode { get; set; } = string.Empty;
    public decimal StatementAmountDue { get; set; }
    public decimal RemainingBalance { get; set; }
    public Guid? CarryStatementId { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateOnly PaymentDate { get; set; }
}
