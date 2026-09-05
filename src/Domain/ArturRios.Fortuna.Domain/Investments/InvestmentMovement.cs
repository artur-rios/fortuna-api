using ArturRios.Fortuna.Domain.Lifecycle;

namespace ArturRios.Fortuna.Domain.Investments;

public enum InvestmentMovementType : short
{
    Contribution = 1,
    Withdrawal = 2,
    Yield = 3,
    Fee = 4
}

public sealed class InvestmentMovement : RecordLifecycleEntity
{
    private InvestmentMovement()
    {
    }

    public InvestmentMovement(
        Investment investment,
        InvestmentMovementType movementType,
        decimal amount,
        DateOnly occurredOn,
        DateTimeOffset createdAt) : base(createdAt)
    {
        Investment = investment ?? throw new ArgumentNullException(nameof(investment));
        if (!Enum.IsDefined(movementType))
        {
            throw new ArgumentOutOfRangeException(nameof(movementType));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "An investment movement amount must be greater than zero.");
        }

        InvestmentId = investment.Id;
        MovementType = movementType;
        Amount = amount;
        OccurredOn = occurredOn;
    }

    public long Id { get; private set; }
    public long InvestmentId { get; private set; }
    public Investment Investment { get; private set; } = null!;
    public InvestmentMovementType MovementType { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly OccurredOn { get; private set; }
}
