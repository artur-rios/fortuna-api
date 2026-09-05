using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class InvestmentMovementTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 23, 30, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData(InvestmentMovementType.Contribution)]
    [InlineData(InvestmentMovementType.Withdrawal)]
    [InlineData(InvestmentMovementType.Yield)]
    [InlineData(InvestmentMovementType.Fee)]
    public void GivenValidMovement_WhenCreated_ThenValuesAreRetained(
        InvestmentMovementType movementType)
    {
        var investment = Investment();

        var movement = new InvestmentMovement(
            investment,
            movementType,
            125.50m,
            new DateOnly(2026, 9, 3),
            Now);

        Assert.Equal(investment, movement.Investment);
        Assert.Equal(movementType, movement.MovementType);
        Assert.Equal(125.50m, movement.Amount);
        Assert.Equal(new DateOnly(2026, 9, 3), movement.OccurredOn);
        Assert.Equal(Now, movement.CreatedAt);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void GivenNonPositiveAmount_WhenCreated_ThenMovementIsRejected(string amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvestmentMovement(
            Investment(),
            InvestmentMovementType.Contribution,
            decimal.Parse(amount),
            new DateOnly(2026, 9, 4),
            Now));
    }

    [UnitFact]
    public void GivenInvalidType_WhenCreated_ThenMovementIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvestmentMovement(
            Investment(),
            (InvestmentMovementType)99,
            10m,
            new DateOnly(2026, 9, 4),
            Now));
    }

    [UnitFact]
    public void GivenMissingInvestment_WhenCreated_ThenMovementIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new InvestmentMovement(
            null!,
            InvestmentMovementType.Contribution,
            10m,
            new DateOnly(2026, 9, 4),
            Now));
    }

    private static Investment Investment()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        return new Investment(
            user,
            "Treasury Bond",
            "Broker",
            InvestmentType.FixedIncome,
            currency,
            Now);
    }
}
