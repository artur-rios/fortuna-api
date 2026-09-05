using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordInvestmentMovementCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid InvestmentId { get; set; }
    public InvestmentMovementType MovementType { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public decimal Position { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public decimal? FundingAmount { get; set; }
    public string? FundingCurrencyCode { get; set; }
    public Guid? TransferId { get; set; }
    public Guid? OutboundTransactionId { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
