using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Domain.Investments;

public sealed class InvestmentValuation : RecordLifecycleEntity
{
    private InvestmentValuation()
    {
    }

    public InvestmentValuation(
        Investment investment,
        decimal value,
        DateOnly valuedOn,
        DateTimeOffset createdAt) : base(createdAt)
    {
        Investment = investment ?? throw new ArgumentNullException(nameof(investment));
        InvestmentId = investment.Id;
        Value = value;
        ValuedOn = valuedOn;
    }

    public long Id { get; private set; }
    public long InvestmentId { get; private set; }
    public Investment Investment { get; private set; } = null!;
    public decimal Value { get; private set; }
    public DateOnly ValuedOn { get; private set; }

    public void ReplaceValue(decimal value, DateTimeOffset updatedAt)
    {
        Value = value;
        MarkUpdated(updatedAt);
    }
}
