using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Planning;

public sealed class EfBudgetStore(AppDbContext context)
    : IBudgetStore,
        IBudgetReader,
        IBudgetUpdater,
        IBudgetLifecycleStore,
        IBudgetConsumptionReader
{
    public async Task<BudgetMutationResult> CreateAsync(
        BudgetCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == creation.UserId,
            cancellationToken);
        var currency = await context.Currencies.SingleOrDefaultAsync(
            item => item.Code == creation.CurrencyCode,
            cancellationToken);
        if (currency is null)
        {
            return Result(BudgetMutationOutcome.CurrencyNotFound);
        }

        var categories = await ResolveCategoriesAsync(
            user.Id,
            creation.CategoryIds,
            cancellationToken);
        if (categories is null)
        {
            return Result(BudgetMutationOutcome.CategoryNotFound);
        }

        var budget = new Budget(
            user,
            creation.Amount,
            currency,
            creation.PeriodType,
            creation.PeriodStart,
            categories,
            creation.IncludeDescendants,
            creation.CreatedAt);
        context.Budgets.Add(budget);
        await context.SaveChangesAsync(cancellationToken);
        return new BudgetMutationResult(
            await SnapshotAsync(
                budget,
                DateOnly.FromDateTime(creation.CreatedAt.UtcDateTime),
                cancellationToken),
            BudgetMutationOutcome.Succeeded);
    }

    public async Task<IReadOnlyCollection<BudgetSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var budgets = await BudgetQuery()
            .Where(item =>
                item.User.PublicId == userId &&
                (includeDeleted || !item.IsDeleted))
            .OrderBy(item => item.PeriodStart)
            .ThenBy(item => item.PublicId)
            .ToArrayAsync(cancellationToken);
        var snapshots = new List<BudgetSnapshot>(budgets.Length);
        foreach (var budget in budgets)
        {
            snapshots.Add(await SnapshotAsync(budget, asOf, cancellationToken));
        }

        return snapshots;
    }

    public async Task<BudgetSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var budget = await BudgetQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id &&
            (includeDeleted || !item.IsDeleted),
            cancellationToken);
        return budget is null ? null : await SnapshotAsync(budget, asOf, cancellationToken);
    }

    public async Task<BudgetConsumptionResult> GetConsumptionAsync(
        Guid userId,
        Guid id,
        DateOnly periodDate,
        CancellationToken cancellationToken)
    {
        var budget = await BudgetQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id &&
            !item.IsDeleted,
            cancellationToken);
        if (budget is null)
        {
            return new BudgetConsumptionResult(null, BudgetConsumptionOutcome.NotFound);
        }

        if (periodDate < budget.PeriodStart)
        {
            return new BudgetConsumptionResult(
                new BudgetConsumptionDetailSnapshot(
                    budget.PublicId,
                    budget.Amount,
                    budget.Currency.Code,
                    periodDate,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    true,
                    []),
                BudgetConsumptionOutcome.PeriodPrecedesBudget);
        }

        var calculation = await CalculateConsumptionAsync(
            budget,
            periodDate,
            cancellationToken);
        return new BudgetConsumptionResult(
            new BudgetConsumptionDetailSnapshot(
                budget.PublicId,
                budget.Amount,
                budget.Currency.Code,
                periodDate,
                calculation.PeriodStart,
                calculation.PeriodEnd,
                calculation.Spent,
                calculation.Remaining,
                calculation.IsExceeded,
                calculation.Overage,
                true,
                calculation.IsFullyConverted,
                calculation.Conversions),
            BudgetConsumptionOutcome.Succeeded);
    }

    public async Task<BudgetMutationResult> UpdateAsync(
        BudgetUpdate update,
        CancellationToken cancellationToken)
    {
        var budget = await BudgetQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == update.UserId &&
            item.PublicId == update.Id &&
            !item.IsDeleted,
            cancellationToken);
        if (budget is null)
        {
            return Result(BudgetMutationOutcome.NotFound);
        }

        var currency = await context.Currencies.SingleOrDefaultAsync(
            item => item.Code == update.CurrencyCode,
            cancellationToken);
        if (currency is null)
        {
            return Result(BudgetMutationOutcome.CurrencyNotFound);
        }

        var categories = await ResolveCategoriesAsync(
            budget.UserId,
            update.CategoryIds,
            cancellationToken);
        if (categories is null)
        {
            return Result(BudgetMutationOutcome.CategoryNotFound);
        }

        budget.UpdateDetails(
            update.Amount,
            currency,
            update.PeriodType,
            update.PeriodStart,
            categories,
            update.IncludeDescendants,
            update.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new BudgetMutationResult(
            await SnapshotAsync(
                budget,
                DateOnly.FromDateTime(update.UpdatedAt.UtcDateTime),
                cancellationToken),
            BudgetMutationOutcome.Succeeded);
    }

    public async Task<BudgetMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var budget = await BudgetQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId && item.PublicId == id,
            cancellationToken);
        if (budget is null)
        {
            return Result(BudgetMutationOutcome.NotFound);
        }

        budget.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new BudgetMutationResult(
            await SnapshotAsync(budget, asOf, cancellationToken),
            BudgetMutationOutcome.Succeeded);
    }

    private IQueryable<Budget> BudgetQuery() => context.Budgets
        .Include(item => item.User)
        .Include(item => item.Currency)
        .Include(item => item.Categories);

    private async Task<List<Category>?> ResolveCategoriesAsync(
        long userId,
        IReadOnlyCollection<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var requested = categoryIds.Distinct().ToArray();
        var categories = await context.Categories
            .Include(item => item.User)
            .Where(item =>
                item.UserId == userId &&
                requested.Contains(item.PublicId) &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);
        return categories.Count == requested.Length ? categories : null;
    }

    private async Task<BudgetSnapshot> SnapshotAsync(
        Budget budget,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var calculation = await CalculateConsumptionAsync(budget, asOf, cancellationToken);
        var consumption = new BudgetConsumptionSnapshot(
            calculation.PeriodStart,
            calculation.PeriodEnd,
            calculation.Spent,
            calculation.Remaining,
            calculation.IsExceeded,
            calculation.Overage,
            calculation.IsFullyConverted);
        return new BudgetSnapshot(
            budget.PublicId,
            budget.Amount,
            budget.Currency.Code,
            budget.PeriodType,
            budget.PeriodStart,
            budget.IncludeDescendants,
            budget.Categories
                .OrderBy(item => item.Name)
                .ThenBy(item => item.PublicId)
                .Select(item => new BudgetCategorySnapshot(item.PublicId, item.Name))
                .ToArray(),
            consumption,
            budget.IsDeleted,
            budget.CreatedAt,
            budget.UpdatedAt);
    }

    private async Task<ConsumptionCalculation> CalculateConsumptionAsync(
        Budget budget,
        DateOnly periodDate,
        CancellationToken cancellationToken)
    {
        var period = PeriodContaining(budget.PeriodStart, budget.PeriodType, periodDate);
        var categoryIds = await CoveredCategoryIdsAsync(budget, cancellationToken);
        var transactions = await context.FinancialTransactions
            .AsNoTracking()
            .Where(item =>
                item.UserId == budget.UserId &&
                categoryIds.Contains(item.CategoryId) &&
                item.Direction == TransactionDirection.Expense &&
                !item.IsDeleted &&
                item.OccurredOn >= period.Start &&
                item.OccurredOn <= period.End &&
                !context.Transfers.Any(transfer =>
                    transfer.OutboundTransactionId == item.Id ||
                    transfer.InboundTransactionId == item.Id))
            .Select(item => new TransactionFigure(
                item.Amount,
                item.Currency.Code,
                item.OccurredOn))
            .ToArrayAsync(cancellationToken);

        var figures = new List<ConvertedFigure>(transactions.Length);
        foreach (var figure in transactions
            .GroupBy(item => new { item.CurrencyCode, item.OccurredOn })
            .Select(group => new TransactionFigure(
                group.Sum(item => item.Amount),
                group.Key.CurrencyCode,
                group.Key.OccurredOn)))
        {
            if (figure.CurrencyCode == budget.Currency.Code)
            {
                figures.Add(new ConvertedFigure(
                    figure.CurrencyCode,
                    figure.Amount,
                    figure.Amount,
                    null,
                    null,
                    null,
                    null));
                continue;
            }

            var rate = await context.ExchangeRates
                .AsNoTracking()
                .Where(item =>
                    item.BaseCurrency.Code == figure.CurrencyCode &&
                    item.QuoteCurrency.Code == budget.Currency.Code &&
                    item.RateDate <= figure.OccurredOn)
                .OrderByDescending(item => item.RateDate)
                .ThenByDescending(item => item.Source)
                .Select(item => new AppliedRate(
                    item.Rate,
                    item.RateDate,
                    item.Source))
                .FirstOrDefaultAsync(cancellationToken);
            if (rate is null)
            {
                figures.Add(new ConvertedFigure(
                    figure.CurrencyCode,
                    figure.Amount,
                    null,
                    null,
                    null,
                    null,
                    FigureConversionMessages.RateUnavailable));
                continue;
            }

            figures.Add(new ConvertedFigure(
                figure.CurrencyCode,
                figure.Amount,
                figure.Amount * rate.Rate,
                rate.Rate,
                rate.RateDate,
                rate.Source,
                null));
        }

        var conversions = figures
            .GroupBy(item => new
            {
                item.SourceCurrencyCode,
                item.AppliedRate,
                item.RateDate,
                item.RateSource,
                item.UnconvertedReason
            })
            .OrderBy(group => group.Key.SourceCurrencyCode)
            .ThenBy(group => group.Key.RateDate)
            .Select(group => new BudgetConversionSnapshot(
                group.Key.SourceCurrencyCode,
                group.Sum(item => item.SourceAmount),
                group.All(item => item.ConvertedAmount.HasValue)
                    ? Round(group.Sum(item => item.ConvertedAmount!.Value), budget)
                    : null,
                group.Key.AppliedRate,
                group.Key.RateDate,
                group.Key.RateSource,
                group.Key.UnconvertedReason))
            .ToArray();
        var fullyConverted = figures.All(item => item.ConvertedAmount.HasValue);
        decimal? roundedSpent = fullyConverted
            ? Round(figures.Sum(item => item.ConvertedAmount!.Value), budget)
            : null;
        return new ConsumptionCalculation(
            period.Start,
            period.End,
            roundedSpent,
            roundedSpent.HasValue ? decimal.Max(budget.Amount - roundedSpent.Value, 0m) : null,
            roundedSpent.HasValue ? roundedSpent.Value > budget.Amount : null,
            roundedSpent.HasValue ? decimal.Max(roundedSpent.Value - budget.Amount, 0m) : null,
            fullyConverted,
            conversions);
    }

    private async Task<HashSet<long>> CoveredCategoryIdsAsync(
        Budget budget,
        CancellationToken cancellationToken)
    {
        var covered = budget.Categories.Select(item => item.Id).ToHashSet();
        if (!budget.IncludeDescendants)
        {
            return covered;
        }

        var hierarchy = await context.Categories
            .AsNoTracking()
            .Where(item => item.UserId == budget.UserId && !item.IsDeleted)
            .Select(item => new CategoryParent(item.Id, item.ParentId))
            .ToArrayAsync(cancellationToken);
        var children = hierarchy.ToLookup(item => item.ParentId);
        var pending = new Queue<long>(covered);
        while (pending.TryDequeue(out var parentId))
        {
            foreach (var child in children[parentId])
            {
                if (covered.Add(child.Id))
                {
                    pending.Enqueue(child.Id);
                }
            }
        }

        return covered;
    }

    private static (DateOnly Start, DateOnly End) PeriodContaining(
        DateOnly anchor,
        BudgetPeriodType periodType,
        DateOnly asOf)
    {
        var monthsPerPeriod = periodType switch
        {
            BudgetPeriodType.Monthly => 1,
            BudgetPeriodType.Quarterly => 3,
            BudgetPeriodType.Yearly => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(periodType))
        };
        var elapsedMonths = (asOf.Year - anchor.Year) * 12 + asOf.Month - anchor.Month;
        var periods = elapsedMonths < 0 ? 0 : elapsedMonths / monthsPerPeriod;
        var start = anchor.AddMonths(periods * monthsPerPeriod);
        if (start > asOf && periods > 0)
        {
            start = anchor.AddMonths((periods - 1) * monthsPerPeriod);
        }

        return (start, start.AddMonths(monthsPerPeriod).AddDays(-1));
    }

    private static BudgetMutationResult Result(BudgetMutationOutcome outcome) => new(null, outcome);

    private static decimal Round(decimal amount, Budget budget) => decimal.Round(
        amount,
        budget.Currency.MinorUnitDigits,
        MidpointRounding.AwayFromZero);

    private sealed record CategoryParent(long Id, long? ParentId);
    private sealed record TransactionFigure(decimal Amount, string CurrencyCode, DateOnly OccurredOn);
    private sealed record AppliedRate(
        decimal Rate,
        DateOnly RateDate,
        ExchangeRateSource Source);
    private sealed record ConvertedFigure(
        string SourceCurrencyCode,
        decimal SourceAmount,
        decimal? ConvertedAmount,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
    private sealed record ConsumptionCalculation(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        decimal? Spent,
        decimal? Remaining,
        bool? IsExceeded,
        decimal? Overage,
        bool IsFullyConverted,
        IReadOnlyCollection<BudgetConversionSnapshot> Conversions);
}
