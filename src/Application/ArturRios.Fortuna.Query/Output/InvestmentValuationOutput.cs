using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class InvestmentValuationOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid InvestmentId { get; set; }
    public decimal Value { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly ValuedOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
