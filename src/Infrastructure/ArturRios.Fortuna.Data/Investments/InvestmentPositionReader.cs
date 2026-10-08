using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Investments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Investments;

// Positions of several investments on a day in two queries: the latest live valuation on or
// before the day plus the live movements that follow it (InvestmentPositionCalculator's rule:
// dated after it, or dated the same day and recorded after it) and are on or before the day.
internal static class InvestmentPositionReader
{
    public static async Task<IReadOnlyDictionary<long, decimal>> CalculateAsync(
        AppDbContext context,
        IReadOnlyCollection<long> investmentIds,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var ids = investmentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, decimal>();
        }

        var latestValuations = (await context.InvestmentValuations
                .AsNoTracking()
                .Where(valuation =>
                    ids.Contains(valuation.InvestmentId) &&
                    !valuation.IsDeleted &&
                    valuation.ValuedOn <= asOf)
                .Select(valuation => new
                {
                    valuation.InvestmentId,
                    valuation.ValuedOn,
                    valuation.UpdatedAt,
                    valuation.Value
                })
                .ToListAsync(cancellationToken))
            .GroupBy(valuation => valuation.InvestmentId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(valuation => valuation.ValuedOn)
                    .ThenByDescending(valuation => valuation.UpdatedAt)
                    .First());
        var movements = await context.InvestmentMovements
            .AsNoTracking()
            .Where(movement =>
                ids.Contains(movement.InvestmentId) &&
                !movement.IsDeleted &&
                movement.OccurredOn <= asOf)
            .Select(movement => new
            {
                movement.InvestmentId,
                movement.OccurredOn,
                movement.CreatedAt,
                Amount = movement.MovementType == InvestmentMovementType.Contribution ||
                    movement.MovementType == InvestmentMovementType.Yield
                        ? movement.Amount
                        : -movement.Amount
            })
            .ToListAsync(cancellationToken);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var latest = latestValuations.GetValueOrDefault(id);

                return (latest?.Value ?? 0m) + movements
                    .Where(movement => movement.InvestmentId == id &&
                        (latest is null ||
                         movement.OccurredOn > latest.ValuedOn ||
                         (movement.OccurredOn == latest.ValuedOn &&
                          movement.CreatedAt > latest.UpdatedAt)))
                    .Sum(movement => movement.Amount);
            });
    }
}
