using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class InvestmentValuationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 2, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenNegativeValue_WhenCreated_ThenValuationRetainsExactValue()
    {
        var investment = Investment();

        var valuation = new InvestmentValuation(
            investment,
            -25.50m,
            new DateOnly(2026, 9, 4),
            Now);

        Assert.Equal(investment, valuation.Investment);
        Assert.Equal(-25.50m, valuation.Value);
        Assert.Equal(new DateOnly(2026, 9, 4), valuation.ValuedOn);
        Assert.Equal(Now, valuation.CreatedAt);
    }

    [UnitFact]
    public void GivenExistingValuation_WhenReplaced_ThenValueAndTimestampChange()
    {
        var valuation = new InvestmentValuation(
            Investment(),
            100m,
            new DateOnly(2026, 9, 4),
            Now);
        var updatedAt = Now.AddMinutes(5);

        valuation.ReplaceValue(125m, updatedAt);

        Assert.Equal(125m, valuation.Value);
        Assert.Equal(updatedAt, valuation.UpdatedAt);
        Assert.Equal(Now, valuation.CreatedAt);
    }

    [UnitFact]
    public void GivenMissingInvestment_WhenCreated_ThenValuationIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new InvestmentValuation(
            null!,
            100m,
            new DateOnly(2026, 9, 4),
            Now));
    }

    [UnitFact]
    public void GivenNoValuation_WhenCalculated_ThenAllLiveMovementsFormPosition()
    {
        var investment = Investment();
        var deleted = Movement(
            investment, InvestmentMovementType.Contribution, 1000m, new DateOnly(2026, 9, 1));
        deleted.SoftDelete(Now);
        var movements = new[]
        {
            Movement(investment, InvestmentMovementType.Contribution, 100m,
                new DateOnly(2026, 9, 1)),
            Movement(investment, InvestmentMovementType.Withdrawal, 30m,
                new DateOnly(2026, 9, 2)),
            Movement(investment, InvestmentMovementType.Yield, 10m,
                new DateOnly(2026, 9, 3)),
            Movement(investment, InvestmentMovementType.Fee, 5m,
                new DateOnly(2026, 9, 4)),
            deleted
        };

        var position = InvestmentPositionCalculator.Calculate(movements, []);

        Assert.Equal(75m, position.Value);
        Assert.False(position.IsIndependentlyValued);
        Assert.Null(position.ValuationValue);
        Assert.Null(position.ValuedOn);
    }

    [UnitFact]
    public void GivenValuation_WhenCalculated_ThenOnlyLaterMovementsAdjustBaseline()
    {
        var investment = Investment();
        var movements = new[]
        {
            Movement(investment, InvestmentMovementType.Contribution, 100m,
                new DateOnly(2026, 9, 1)),
            Movement(investment, InvestmentMovementType.Yield, 10m,
                new DateOnly(2026, 9, 3)),
            Movement(investment, InvestmentMovementType.Fee, 4m,
                new DateOnly(2026, 9, 4))
        };
        var valuations = new[]
        {
            Valuation(investment, 105m, new DateOnly(2026, 9, 2)),
            Valuation(investment, 120m, new DateOnly(2026, 9, 3))
        };

        var position = InvestmentPositionCalculator.Calculate(movements, valuations);

        Assert.Equal(116m, position.Value);
        Assert.True(position.IsIndependentlyValued);
        Assert.Equal(120m, position.ValuationValue);
        Assert.Equal(new DateOnly(2026, 9, 3), position.ValuedOn);
    }

    [UnitFact]
    public void GivenDeletedLatestValuation_WhenCalculated_ThenPreviousValuationIsUsed()
    {
        var investment = Investment();
        var previous = Valuation(investment, 100m, new DateOnly(2026, 9, 2));
        var deletedLatest = Valuation(investment, 500m, new DateOnly(2026, 9, 4));
        deletedLatest.SoftDelete(Now);

        var position = InvestmentPositionCalculator.Calculate([], [previous, deletedLatest]);

        Assert.Equal(100m, position.Value);
        Assert.Equal(new DateOnly(2026, 9, 2), position.ValuedOn);
    }

    [UnitFact]
    public void GivenMissingCollections_WhenCalculated_ThenInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InvestmentPositionCalculator.Calculate(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            InvestmentPositionCalculator.Calculate([], null!));
    }

    private static InvestmentValuation Valuation(
        Investment investment,
        decimal value,
        DateOnly valuedOn) => new(investment, value, valuedOn, Now);

    private static InvestmentMovement Movement(
        Investment investment,
        InvestmentMovementType type,
        decimal amount,
        DateOnly occurredOn) => new(investment, type, amount, occurredOn, Now);

    private static Investment Investment()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        return new Investment(
            user,
            "Fund",
            null,
            InvestmentType.Fund,
            currency,
            Now);
    }
}
