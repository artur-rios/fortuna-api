using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Shared.Classification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Classification;

public sealed class EfCounterpartyStore(AppDbContext context)
    : ICounterpartyStore,
        ICounterpartyReader,
        ICounterpartyUpdater,
        ICounterpartyLifecycleStore,
        ICounterpartyMerger,
        ICounterpartyCategorySuggester
{
    private const string LiveNameIndex = "ix_counterparty_user_id_normalized_name";

    public async Task<CounterpartyCreationResult> CreateAsync(
        CounterpartyCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == creation.UserId,
            cancellationToken);
        var existing = await FindLiveByNameAsync(
            user.Id,
            creation.Name,
            cancellationToken);
        if (existing is not null)
        {
            return new CounterpartyCreationResult(
                Snapshot(existing),
                Reused: true,
                CounterpartyMutationOutcome.Succeeded);
        }

        var counterparty = new Counterparty(user, creation.Name, creation.CreatedAt);
        context.Counterparties.Add(counterparty);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            context.Entry(counterparty).State = EntityState.Detached;
            existing = await FindLiveByNameAsync(user.Id, creation.Name, cancellationToken);
            return new CounterpartyCreationResult(
                existing is null ? null : Snapshot(existing),
                Reused: existing is not null,
                existing is null
                    ? CounterpartyMutationOutcome.DuplicateName
                    : CounterpartyMutationOutcome.Succeeded);
        }

        return new CounterpartyCreationResult(
            Snapshot(counterparty),
            Reused: false,
            CounterpartyMutationOutcome.Succeeded);
    }

    public async Task<IReadOnlyCollection<CounterpartySnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        CancellationToken cancellationToken) => await context.Counterparties
        .AsNoTracking()
        .Where(item =>
            item.User.PublicId == userId &&
            (includeDeleted || !item.IsDeleted))
        .OrderBy(item => item.Name)
        .ThenBy(item => item.PublicId)
        .Select(item => new CounterpartySnapshot(
            item.PublicId,
            item.Name,
            item.IsDeleted,
            item.CreatedAt,
            item.UpdatedAt))
        .ToArrayAsync(cancellationToken);

    public async Task<CounterpartyMutationResult> UpdateAsync(
        CounterpartyUpdate update,
        CancellationToken cancellationToken)
    {
        var counterparty = await FindTrackedAsync(update.UserId, update.Id, cancellationToken);
        if (counterparty is null || counterparty.IsDeleted)
        {
            return MutationResult(CounterpartyMutationOutcome.NotFound);
        }

        var normalizedName = update.Name.Trim().ToUpperInvariant();
        if (await context.Counterparties.AnyAsync(item =>
            item.Id != counterparty.Id &&
            item.UserId == counterparty.UserId &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken))
        {
            return MutationResult(CounterpartyMutationOutcome.DuplicateName);
        }

        counterparty.Rename(update.Name, update.UpdatedAt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            context.Entry(counterparty).State = EntityState.Detached;
            return MutationResult(CounterpartyMutationOutcome.DuplicateName);
        }

        return new CounterpartyMutationResult(
            Snapshot(counterparty),
            CounterpartyMutationOutcome.Succeeded);
    }

    public async Task<CounterpartyMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var counterparty = await FindTrackedAsync(userId, id, cancellationToken);
        if (counterparty is null)
        {
            return MutationResult(CounterpartyMutationOutcome.NotFound);
        }

        counterparty.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new CounterpartyMutationResult(
            Snapshot(counterparty),
            CounterpartyMutationOutcome.Succeeded);
    }

    public async Task<CounterpartyMergeResult> MergeAsync(
        CounterpartyMerge merge,
        CancellationToken cancellationToken)
    {
        if (merge.SourceId == merge.TargetId)
        {
            return MergeResult(CounterpartyMergeOutcome.SameCounterparty);
        }

        var counterparties = await context.Counterparties
            .Where(item =>
                item.User.PublicId == merge.UserId &&
                !item.IsDeleted &&
                (item.PublicId == merge.SourceId || item.PublicId == merge.TargetId))
            .ToArrayAsync(cancellationToken);
        var source = counterparties.SingleOrDefault(item => item.PublicId == merge.SourceId);
        var target = counterparties.SingleOrDefault(item => item.PublicId == merge.TargetId);
        if (source is null || target is null)
        {
            return MergeResult(CounterpartyMergeOutcome.NotFound);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var reassignedCount = await context.FinancialTransactions
            .Where(item =>
                item.UserId == source.UserId &&
                item.CounterpartyId == source.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CounterpartyId, target.Id)
                .SetProperty(item => item.UpdatedAt, merge.ChangedAt),
                cancellationToken);
        await context.RecurringTransactions
            .Where(item =>
                item.UserId == source.UserId &&
                item.CounterpartyId == source.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CounterpartyId, target.Id)
                .SetProperty(item => item.UpdatedAt, merge.ChangedAt),
                cancellationToken);
        source.SoftDelete(merge.ChangedAt);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CounterpartyMergeResult(
            source.PublicId,
            target.PublicId,
            reassignedCount,
            CounterpartyMergeOutcome.Succeeded);
    }

    public async Task<CounterpartyCategorySuggestionResult> SuggestCategoryAsync(
        Guid userId,
        Guid counterpartyId,
        CancellationToken cancellationToken)
    {
        var counterparty = await context.Counterparties
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == userId &&
                item.PublicId == counterpartyId &&
                !item.IsDeleted)
            .Select(item => new { item.Id, item.UserId, item.PublicId })
            .SingleOrDefaultAsync(cancellationToken);
        if (counterparty is null)
        {
            return SuggestionResult(CounterpartyCategorySuggestionOutcome.NotFound);
        }

        var category = await context.FinancialTransactions
            .AsNoTracking()
            .Where(item =>
                item.UserId == counterparty.UserId &&
                item.CounterpartyId == counterparty.Id &&
                !item.IsDeleted &&
                !item.Category.IsDeleted)
            .OrderByDescending(item => item.OccurredOn)
            .ThenByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new { item.Category.PublicId, item.Category.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return new CounterpartyCategorySuggestionResult(
            counterparty.PublicId,
            category?.PublicId,
            category?.Name,
            CounterpartyCategorySuggestionOutcome.Succeeded);
    }

    private Task<Counterparty?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.Counterparties.SingleOrDefaultAsync(
        item => item.User.PublicId == userId && item.PublicId == id,
        cancellationToken);

    private Task<Counterparty?> FindLiveByNameAsync(
        long userId,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToUpperInvariant();
        return context.Counterparties
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.UserId == userId &&
                item.NormalizedName == normalizedName &&
                !item.IsDeleted,
                cancellationToken);
    }

    private static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LiveNameIndex
        };

    private static CounterpartySnapshot Snapshot(Counterparty counterparty) => new(
        counterparty.PublicId,
        counterparty.Name,
        counterparty.IsDeleted,
        counterparty.CreatedAt,
        counterparty.UpdatedAt);

    private static CounterpartyMutationResult MutationResult(
        CounterpartyMutationOutcome outcome) => new(null, outcome);

    private static CounterpartyMergeResult MergeResult(
        CounterpartyMergeOutcome outcome) => new(null, null, 0, outcome);

    private static CounterpartyCategorySuggestionResult SuggestionResult(
        CounterpartyCategorySuggestionOutcome outcome) => new(null, null, null, outcome);
}
