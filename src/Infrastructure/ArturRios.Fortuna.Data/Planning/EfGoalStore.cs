using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Planning;

public sealed class EfGoalStore(AppDbContext context)
    : IGoalStore, IGoalReader, IGoalUpdater, IGoalLifecycleStore, IGoalProgressReader
{
    public async Task<GoalMutationResult> CreateAsync(
        GoalCreation creation,
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
            return Result(GoalMutationOutcome.CurrencyNotFound);
        }

        var resources = await ResolveResourcesAsync(
            user.Id,
            creation.AccountIds,
            creation.InvestmentIds,
            cancellationToken);
        if (resources is null)
        {
            return Result(GoalMutationOutcome.ResourceNotFound);
        }

        var goal = new Goal(
            user,
            creation.Name,
            creation.TargetAmount,
            currency,
            creation.TargetDate,
            resources.Value.Accounts,
            resources.Value.Investments,
            creation.CreatedAt);
        context.Goals.Add(goal);
        await context.SaveChangesAsync(cancellationToken);
        return new GoalMutationResult(
            await SnapshotAsync(
                goal,
                DateOnly.FromDateTime(creation.CreatedAt.UtcDateTime),
                cancellationToken),
            GoalMutationOutcome.Succeeded);
    }

    public async Task<IReadOnlyCollection<GoalSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var goals = await GoalQuery().Where(item =>
                item.User.PublicId == userId && (includeDeleted || !item.IsDeleted))
            .OrderBy(item => item.TargetDate)
            .ThenBy(item => item.PublicId)
            .ToArrayAsync(cancellationToken);
        var snapshots = new List<GoalSnapshot>(goals.Length);
        foreach (var goal in goals)
        {
            snapshots.Add(await SnapshotAsync(goal, asOf, cancellationToken));
        }

        return snapshots;
    }

    public async Task<GoalSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var goal = await GoalQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id &&
            (includeDeleted || !item.IsDeleted),
            cancellationToken);
        return goal is null ? null : await SnapshotAsync(goal, asOf, cancellationToken);
    }

    public async Task<GoalMutationResult> UpdateAsync(
        GoalUpdate update,
        CancellationToken cancellationToken)
    {
        var goal = await GoalQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == update.UserId &&
            item.PublicId == update.Id &&
            !item.IsDeleted,
            cancellationToken);
        if (goal is null)
        {
            return Result(GoalMutationOutcome.NotFound);
        }

        var currency = await context.Currencies.SingleOrDefaultAsync(
            item => item.Code == update.CurrencyCode,
            cancellationToken);
        if (currency is null)
        {
            return Result(GoalMutationOutcome.CurrencyNotFound);
        }

        var resources = await ResolveResourcesAsync(
            goal.UserId,
            update.AccountIds,
            update.InvestmentIds,
            cancellationToken);
        if (resources is null)
        {
            return Result(GoalMutationOutcome.ResourceNotFound);
        }

        goal.UpdateDetails(
            update.Name,
            update.TargetAmount,
            currency,
            update.TargetDate,
            resources.Value.Accounts,
            resources.Value.Investments,
            update.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new GoalMutationResult(
            await SnapshotAsync(
                goal,
                DateOnly.FromDateTime(update.UpdatedAt.UtcDateTime),
                cancellationToken),
            GoalMutationOutcome.Succeeded);
    }

    public async Task<GoalProgressResult> GetProgressAsync(
        Guid userId,
        Guid id,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var goal = await GoalQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id &&
            !item.IsDeleted,
            cancellationToken);
        return goal is null
            ? new GoalProgressResult(null, GoalProgressOutcome.NotFound)
            : new GoalProgressResult(
                await CalculateProgressAsync(goal, asOf, cancellationToken),
                GoalProgressOutcome.Succeeded);
    }

    public async Task<GoalMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var goal = await GoalQuery().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId && item.PublicId == id,
            cancellationToken);
        if (goal is null)
        {
            return Result(GoalMutationOutcome.NotFound);
        }

        goal.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new GoalMutationResult(
            await SnapshotAsync(goal, asOf, cancellationToken),
            GoalMutationOutcome.Succeeded);
    }

    private IQueryable<Goal> GoalQuery() => context.Goals
        .Include(item => item.User)
        .Include(item => item.Currency)
        .Include(item => item.Accounts).ThenInclude(item => item.Currency)
        .Include(item => item.Investments).ThenInclude(item => item.Currency);

    private async Task<(List<FinancialAccount> Accounts, List<Investment> Investments)?>
        ResolveResourcesAsync(
            long userId,
            IReadOnlyCollection<Guid> accountIds,
            IReadOnlyCollection<Guid> investmentIds,
            CancellationToken cancellationToken)
    {
        var requestedAccounts = accountIds.Distinct().ToArray();
        var accounts = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .Where(item =>
                item.UserId == userId &&
                requestedAccounts.Contains(item.PublicId) &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);
        var requestedInvestments = investmentIds.Distinct().ToArray();
        var investments = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .Where(item =>
                item.UserId == userId &&
                requestedInvestments.Contains(item.PublicId) &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);
        return accounts.Count == requestedAccounts.Length &&
            investments.Count == requestedInvestments.Length
                ? (accounts, investments)
                : null;
    }

    private async Task<GoalSnapshot> SnapshotAsync(
        Goal goal,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var detail = await CalculateProgressAsync(goal, asOf, cancellationToken);
        var progress = new GoalProgressSnapshot(
            detail.CurrentAmount,
            detail.Shortfall,
            detail.ProportionReached,
            detail.IsReached,
            detail.IsFullyConverted);
        return new GoalSnapshot(
            goal.PublicId,
            goal.Name,
            goal.TargetAmount,
            goal.Currency.Code,
            goal.TargetDate,
            goal.Accounts.OrderBy(item => item.Name)
                .Select(item => new GoalResourceSnapshot(item.PublicId, item.Name)).ToArray(),
            goal.Investments.OrderBy(item => item.Instrument)
                .Select(item => new GoalResourceSnapshot(item.PublicId, item.Instrument)).ToArray(),
            progress,
            goal.IsDeleted,
            goal.CreatedAt,
            goal.UpdatedAt);
    }

    private async Task<GoalProgressDetailSnapshot> CalculateProgressAsync(
        Goal goal,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var resources = new List<GoalResourceProgressSnapshot>();
        foreach (var account in goal.Accounts.OrderBy(item => item.Name))
        {
            if (account.IsDeleted)
            {
                resources.Add(ExcludedResource(
                    account.PublicId,
                    account.Name,
                    GoalResourceType.Account,
                    account.Currency.Code));
                continue;
            }

            var movements = await context.FinancialTransactions.AsNoTracking()
                .Where(item =>
                    item.FinancialAccountId == account.Id &&
                    !item.IsDeleted &&
                    item.OccurredOn <= asOf)
                .Select(item => (decimal?)(item.Direction ==
                    TransactionDirection.Earning
                        ? item.Amount
                        : -item.Amount))
                .SumAsync(cancellationToken) ?? 0m;
            resources.Add(await ConvertResourceAsync(
                account.PublicId,
                account.Name,
                GoalResourceType.Account,
                account.OpeningBalance + movements,
                account.Currency.Code,
                goal,
                asOf,
                cancellationToken));
        }

        foreach (var investment in goal.Investments.OrderBy(item => item.Instrument))
        {
            if (investment.IsDeleted)
            {
                resources.Add(ExcludedResource(
                    investment.PublicId,
                    investment.Instrument,
                    GoalResourceType.Investment,
                    investment.Currency.Code));
                continue;
            }

            var latest = await context.InvestmentValuations.AsNoTracking()
                .Where(item =>
                    item.InvestmentId == investment.Id &&
                    !item.IsDeleted &&
                    item.ValuedOn <= asOf)
                .OrderByDescending(item => item.ValuedOn)
                .Select(item => new { item.Value, item.ValuedOn })
                .FirstOrDefaultAsync(cancellationToken);
            var position = latest?.Value ?? 0m;
            position += await context.InvestmentMovements.AsNoTracking()
                .Where(item =>
                    item.InvestmentId == investment.Id &&
                    !item.IsDeleted &&
                    item.OccurredOn <= asOf &&
                    item.OccurredOn > (latest == null ? DateOnly.MinValue : latest.ValuedOn))
                .Select(item => (decimal?)(
                    item.MovementType == InvestmentMovementType.Contribution ||
                    item.MovementType == InvestmentMovementType.Yield
                        ? item.Amount
                        : -item.Amount))
                .SumAsync(cancellationToken) ?? 0m;
            resources.Add(await ConvertResourceAsync(
                investment.PublicId,
                investment.Instrument,
                GoalResourceType.Investment,
                position,
                investment.Currency.Code,
                goal,
                asOf,
                cancellationToken));
        }

        var included = resources.Where(item => item.IsIncluded).ToArray();
        var fullyConverted = included.All(item => item.ConvertedAmount.HasValue);
        decimal? current = fullyConverted
            ? Round(included.Sum(item => item.ConvertedAmount!.Value), goal)
            : null;
        var daysRemaining = goal.TargetDate.DayNumber - asOf.DayNumber;
        return new GoalProgressDetailSnapshot(
            goal.PublicId,
            goal.TargetAmount,
            goal.Currency.Code,
            goal.TargetDate,
            asOf,
            current,
            current.HasValue ? decimal.Max(goal.TargetAmount - current.Value, 0m) : null,
            current.HasValue
                ? decimal.Round(decimal.Max(current.Value, 0m) / goal.TargetAmount, 4)
                : null,
            current.HasValue ? current.Value >= goal.TargetAmount : null,
            daysRemaining,
            current.HasValue ? daysRemaining < 0 && current.Value < goal.TargetAmount : null,
            fullyConverted,
            resources);
    }

    private async Task<GoalResourceProgressSnapshot> ConvertResourceAsync(
        Guid id,
        string name,
        GoalResourceType resourceType,
        decimal amount,
        string sourceCurrencyCode,
        Goal goal,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        if (sourceCurrencyCode == goal.Currency.Code)
        {
            return new GoalResourceProgressSnapshot(
                id,
                name,
                resourceType,
                sourceCurrencyCode,
                amount,
                Round(amount, goal),
                null,
                null,
                null,
                true,
                null,
                null);
        }

        var rate = await context.ExchangeRates.AsNoTracking()
            .Where(item =>
                item.BaseCurrency.Code == sourceCurrencyCode &&
                item.QuoteCurrency.Code == goal.Currency.Code &&
                item.RateDate <= asOf)
            .OrderByDescending(item => item.RateDate)
            .ThenByDescending(item => item.Source)
            .Select(item => new AppliedRate(item.Rate, item.RateDate, item.Source))
            .FirstOrDefaultAsync(cancellationToken);
        return new GoalResourceProgressSnapshot(
            id,
            name,
            resourceType,
            sourceCurrencyCode,
            amount,
            rate is null ? null : Round(amount * rate.Rate, goal),
            rate?.Rate,
            rate?.RateDate,
            rate?.Source,
            true,
            null,
            rate is null ? FigureConversionMessages.RateUnavailable : null);
    }

    private static GoalMutationResult Result(GoalMutationOutcome outcome) => new(null, outcome);

    private static GoalResourceProgressSnapshot ExcludedResource(
        Guid id,
        string name,
        GoalResourceType resourceType,
        string currencyCode) => new(
            id,
            name,
            resourceType,
            currencyCode,
            null,
            null,
            null,
            null,
            null,
            false,
            GoalMessages.ResourceDeleted,
            null);

    private static decimal Round(decimal amount, Goal goal) => decimal.Round(
        amount,
        goal.Currency.MinorUnitDigits,
        MidpointRounding.AwayFromZero);

    private sealed record AppliedRate(
        decimal Rate,
        DateOnly RateDate,
        ExchangeRateSource Source);
}
