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
                (latestValuation is null || movement.OccurredOn > latestValuation.ValuedOn))
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
}

public sealed record InvestmentPosition(
    decimal Value,
    bool IsIndependentlyValued,
    decimal? ValuationValue,
    DateOnly? ValuedOn);
