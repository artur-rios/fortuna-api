namespace ArturRios.Fortuna.Domain.Investments;

public static class InvestmentPositionCalculator
{
    public static InvestmentPosition Calculate(
        IEnumerable<InvestmentMovement> movements,
        IEnumerable<InvestmentValuation> valuations)
    {
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(valuations);

        var latestValuation = valuations
            .Where(valuation => !valuation.IsDeleted)
            .OrderByDescending(valuation => valuation.ValuedOn)
            .ThenByDescending(valuation => valuation.UpdatedAt)
            .FirstOrDefault();
        var movementPosition = movements
            .Where(movement =>
                !movement.IsDeleted &&
                (latestValuation is null || FollowsValuation(movement, latestValuation)))
            .Sum(movement =>
                movement.MovementType == InvestmentMovementType.Contribution ||
                movement.MovementType == InvestmentMovementType.Yield
                    ? movement.Amount
                    : -movement.Amount);
        var position = (latestValuation?.Value ?? 0m) + movementPosition;

        return new InvestmentPosition(
            position,
            latestValuation is not null,
            latestValuation?.Value,
            latestValuation?.ValuedOn);
    }

    /// <summary>
    /// Whether a movement is not yet reflected in a valuation. A valuation reflects every
    /// movement dated before it; a movement on the valuation day is reflected only when it was
    /// recorded before the valuation was last set.
    /// </summary>
    public static bool FollowsValuation(InvestmentMovement movement, InvestmentValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(valuation);

        return movement.OccurredOn > valuation.ValuedOn ||
            (movement.OccurredOn == valuation.ValuedOn && movement.CreatedAt > valuation.UpdatedAt);
    }
}

public sealed record InvestmentPosition(
    decimal Value,
    bool IsIndependentlyValued,
    decimal? ValuationValue,
    DateOnly? ValuedOn);
