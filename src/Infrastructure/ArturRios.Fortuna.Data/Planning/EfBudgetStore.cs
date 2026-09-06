using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Planning;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Planning;

public sealed class EfBudgetStore(AppDbContext context)
    : IBudgetStore, IBudgetReader, IBudgetUpdater, IBudgetLifecycleStore
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
        var period = PeriodContaining(budget.PeriodStart, budget.PeriodType, asOf);
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

        var spent = 0m;
        var fullyConverted = true;
        foreach (var figure in transactions)
        {
            if (figure.CurrencyCode == budget.Currency.Code)
            {
                spent += figure.Amount;
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
                .Select(item => (decimal?)item.Rate)
                .FirstOrDefaultAsync(cancellationToken);
            if (!rate.HasValue)
            {
                fullyConverted = false;
                continue;
            }

            spent += figure.Amount * rate.Value;
        }

        decimal? roundedSpent = fullyConverted
            ? decimal.Round(spent, budget.Currency.MinorUnitDigits, MidpointRounding.AwayFromZero)
            : null;
        var consumption = new BudgetConsumptionSnapshot(
            period.Start,
            period.End,
            roundedSpent,
            roundedSpent.HasValue ? decimal.Max(budget.Amount - roundedSpent.Value, 0m) : null,
            roundedSpent.HasValue ? roundedSpent.Value > budget.Amount : null,
            roundedSpent.HasValue ? decimal.Max(roundedSpent.Value - budget.Amount, 0m) : null,
            fullyConverted);
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

    private sealed record CategoryParent(long Id, long? ParentId);
    private sealed record TransactionFigure(decimal Amount, string CurrencyCode, DateOnly OccurredOn);
}
