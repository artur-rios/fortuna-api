using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordInvestmentValuationCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid InvestmentId { get; set; }
    public decimal Value { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly ValuedOn { get; set; }
    public bool ReplacedExisting { get; set; }
    public decimal Position { get; set; }
    public bool IsIndependentlyValued { get; set; }
    public decimal? LatestValuationValue { get; set; }
    public DateOnly? LatestValuationDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
