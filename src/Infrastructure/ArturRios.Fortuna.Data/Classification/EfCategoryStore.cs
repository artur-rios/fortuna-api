using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Attachments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Classification;

public sealed class EfCategoryStore(
    AppDbContext context,
    IAttachmentLifecycleStore attachments)
    : ICategoryStore,
        ICategoryReader,
        ICategoryUpdater,
        ICategoryTransactionReassigner,
        ICategoryLifecycleStore
{
    private const string RootSiblingNameIndex = "ix_category_user_id_normalized_name";
    private const string NestedSiblingNameIndex =
        "ix_category_user_id_parent_id_normalized_name";

    public async Task<CategoryCreationResult> CreateAsync(
        CategoryCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            profile => profile.PublicId == creation.UserId,
            cancellationToken);

        Category? parent = null;
        if (creation.ParentId.HasValue)
        {
            parent = await context.Categories
                .Include(category => category.User)
                .SingleOrDefaultAsync(category =>
                    category.PublicId == creation.ParentId.Value &&
                    category.UserId == user.Id &&
                    !category.IsDeleted,
                    cancellationToken);
            if (parent is null)
            {
                return Result(CategoryCreationOutcome.ParentNotFound);
            }

            if (await ParentChainHasCycleAsync(user.Id, parent.Id, cancellationToken))
            {
                return Result(CategoryCreationOutcome.CycleDetected);
            }
        }

        var normalizedName = creation.Name.Trim().ToUpperInvariant();
        var duplicate = await context.Categories.AnyAsync(category =>
            category.UserId == user.Id &&
            category.ParentId == (parent == null ? null : parent.Id) &&
            category.NormalizedName == normalizedName &&
            !category.IsDeleted,
            cancellationToken);
        if (duplicate)
        {
            return Result(CategoryCreationOutcome.DuplicateSiblingName);
        }

        var category = new Category(user, creation.Name, creation.CreatedAt, parent);
        context.Categories.Add(category);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            DatabaseException.IsUniqueViolation(exception, RootSiblingNameIndex, NestedSiblingNameIndex))
        {
            context.Entry(category).State = EntityState.Detached;
            return Result(CategoryCreationOutcome.DuplicateSiblingName);
        }

        return new CategoryCreationResult(
            new CategorySnapshot(
                category.PublicId,
                user.PublicId,
                category.Name,
                parent?.PublicId,
                category.IsDeleted,
                category.CreatedAt,
                category.UpdatedAt),
            CategoryCreationOutcome.Succeeded);
    }

    public async Task<IReadOnlyCollection<CategoryReadSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        bool includeUsageCounts,
        CancellationToken cancellationToken) => await context.Categories
        .AsNoTracking()
        .Where(category =>
            category.User.PublicId == userId &&
            (includeDeleted || !category.IsDeleted))
        .Select(category => new CategoryReadSnapshot(
            category.PublicId,
            category.Name,
            category.Parent == null ? null : category.Parent.PublicId,
            category.IsDeleted,
            includeUsageCounts
                ? context.FinancialTransactions.Count(transaction =>
                    transaction.CategoryId == category.Id && !transaction.IsDeleted)
                : 0,
            category.CreatedAt,
            category.UpdatedAt))
        .ToArrayAsync(cancellationToken);

    public async Task<CategoryUpdateResult> UpdateAsync(
        CategoryUpdate update,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == update.UserId &&
                item.PublicId == update.Id &&
                !item.IsDeleted,
                cancellationToken);
        if (category is null)
        {
            return UpdateResult(CategoryUpdateOutcome.NotFound);
        }

        Category? parent = null;
        if (update.ParentId.HasValue)
        {
            parent = await context.Categories
                .Include(item => item.User)
                .SingleOrDefaultAsync(item =>
                    item.PublicId == update.ParentId.Value &&
                    item.UserId == category.UserId &&
                    !item.IsDeleted,
                    cancellationToken);
            if (parent is null)
            {
                return UpdateResult(CategoryUpdateOutcome.ParentNotFound);
            }

            if (await ParentAssignmentHasCycleAsync(
                category.UserId,
                category.Id,
                parent.Id,
                cancellationToken))
            {
                return UpdateResult(CategoryUpdateOutcome.CycleDetected);
            }
        }

        var normalizedName = update.Name.Trim().ToUpperInvariant();
        var duplicate = await context.Categories.AnyAsync(item =>
            item.Id != category.Id &&
            item.UserId == category.UserId &&
            item.ParentId == (parent == null ? null : parent.Id) &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken);
        if (duplicate)
        {
            return UpdateResult(CategoryUpdateOutcome.DuplicateSiblingName);
        }

        category.UpdateDetails(update.Name, parent, update.UpdatedAt);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            DatabaseException.IsUniqueViolation(exception, RootSiblingNameIndex, NestedSiblingNameIndex))
        {
            context.Entry(category).State = EntityState.Detached;
            return UpdateResult(CategoryUpdateOutcome.DuplicateSiblingName);
        }

        return new CategoryUpdateResult(
            new CategorySnapshot(
                category.PublicId,
                category.User.PublicId,
                category.Name,
                parent?.PublicId,
                category.IsDeleted,
                category.CreatedAt,
                category.UpdatedAt),
            CategoryUpdateOutcome.Succeeded);
    }

    public async Task<CategoryTransactionReassignmentResult> ReassignAsync(
        CategoryTransactionReassignment reassignment,
        CancellationToken cancellationToken)
    {
        if (reassignment.SourceCategoryId == reassignment.TargetCategoryId)
        {
            return ReassignmentResult(CategoryTransactionReassignmentOutcome.SameCategory);
        }

        var categories = await context.Categories
            .AsNoTracking()
            .Where(category =>
                category.User.PublicId == reassignment.UserId &&
                !category.IsDeleted &&
                (category.PublicId == reassignment.SourceCategoryId ||
                    category.PublicId == reassignment.TargetCategoryId))
            .Select(category => new CategoryIdentity(
                category.Id,
                category.UserId,
                category.PublicId))
            .ToArrayAsync(cancellationToken);
        var source = categories.SingleOrDefault(category =>
            category.PublicId == reassignment.SourceCategoryId);
        var target = categories.SingleOrDefault(category =>
            category.PublicId == reassignment.TargetCategoryId);
        if (source is null || target is null)
        {
            return ReassignmentResult(CategoryTransactionReassignmentOutcome.CategoryNotFound);
        }

        var sourceCategoryIds = new HashSet<long> { source.Id };
        if (reassignment.IncludeDescendants)
        {
            var hierarchy = await context.Categories
                .AsNoTracking()
                .Where(category => category.UserId == source.UserId)
                .Select(category => new CategoryParent(category.Id, category.ParentId))
                .ToArrayAsync(cancellationToken);
            var children = hierarchy.ToLookup(category => category.ParentId);
            var pending = new Queue<long>();
            pending.Enqueue(source.Id);

            while (pending.TryDequeue(out var parentId))
            {
                foreach (var child in children[parentId])
                {
                    if (sourceCategoryIds.Add(child.Id))
                    {
                        pending.Enqueue(child.Id);
                    }
                }
            }
        }

        sourceCategoryIds.Remove(target.Id);
        var reassignedCount = await context.FinancialTransactions
            .Where(transaction =>
                transaction.UserId == source.UserId &&
                !transaction.IsDeleted &&
                sourceCategoryIds.Contains(transaction.CategoryId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(transaction => transaction.CategoryId, target.Id)
                .SetProperty(transaction => transaction.UpdatedAt, reassignment.ChangedAt),
                cancellationToken);

        return new CategoryTransactionReassignmentResult(
            reassignedCount,
            CategoryTransactionReassignmentOutcome.Succeeded);
    }

    public async Task<CategoryLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var category = await FindTrackedAsync(userId, id, cancellationToken);
        if (category is null)
        {
            return LifecycleResult(CategoryLifecycleOutcome.NotFound);
        }

        var subtree = await CategorySubtreeAsync(category, cancellationToken);
        var deletion = category.SoftDelete(changedAt);
        foreach (var descendant in subtree.Where(item => item.Id != category.Id))
        {
            descendant.SoftDeleteFromCascade(deletion.CascadeId, changedAt);
        }

        await context.SaveChangesAsync(cancellationToken);
        return LifecycleResult(CategoryLifecycleOutcome.Succeeded, category.PublicId);
    }

    public async Task<CategoryLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var category = await FindTrackedAsync(userId, id, cancellationToken);
        if (category is null)
        {
            return LifecycleResult(CategoryLifecycleOutcome.NotFound);
        }

        if (!category.IsDeleted)
        {
            return LifecycleResult(CategoryLifecycleOutcome.RestoreRequiresSoftDeletion);
        }

        var subtree = await CategorySubtreeAsync(category, cancellationToken);
        var cascadeId = category.Restore(changedAt);
        foreach (var descendant in subtree.Where(item => item.Id != category.Id))
        {
            descendant.RestoreFromCascade(cascadeId, changedAt);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            DatabaseException.IsUniqueViolation(exception, RootSiblingNameIndex, NestedSiblingNameIndex))
        {
            foreach (var item in subtree)
            {
                context.Entry(item).State = EntityState.Detached;
            }

            return LifecycleResult(CategoryLifecycleOutcome.DuplicateSiblingName);
        }

        return LifecycleResult(CategoryLifecycleOutcome.Succeeded, category.PublicId);
    }

    public async Task<CategoryLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var category = await FindTrackedAsync(userId, id, cancellationToken);
        if (category is null)
        {
            return LifecycleResult(CategoryLifecycleOutcome.NotFound);
        }

        var subtree = await CategorySubtreeAsync(category, cancellationToken);
        var categoryIds = subtree.Select(item => item.Id).ToArray();
        var recurringTransactions = await context.RecurringTransactions
            .Where(item => categoryIds.Contains(item.CategoryId))
            .ToListAsync(cancellationToken);
        var recurringTransactionIds = recurringTransactions.Select(item => item.Id).ToArray();
        var transactions = await context.FinancialTransactions
            .Where(item =>
                categoryIds.Contains(item.CategoryId) ||
                (item.RecurringTransactionId.HasValue &&
                    recurringTransactionIds.Contains(item.RecurringTransactionId.Value)))
            .ToListAsync(cancellationToken);
        var liveTransactionCount =
            transactions.Count(item => !item.IsDeleted) +
            recurringTransactions.Count(item => !item.IsDeleted);
        try
        {
            foreach (var item in subtree)
            {
                item.EnsureHardDeletionAllowed(
                    item.Id == category.Id && liveTransactionCount > 0
                        ? ["transactions"]
                        : []);
            }
        }
        catch (RecordLifecycleConflictException exception)
        {
            return exception.Conflict switch
            {
                RecordLifecycleConflict.HardDeleteRequiresSoftDeletion => LifecycleResult(
                    CategoryLifecycleOutcome.HardDeleteRequiresSoftDeletion),
                RecordLifecycleConflict.HardDeleteHasLiveReferences => LifecycleResult(
                    CategoryLifecycleOutcome.HardDeleteHasLiveTransactions,
                    category.PublicId,
                    liveTransactionCount),
                _ => throw new InvalidOperationException(
                    "An unexpected lifecycle conflict prevented hard deletion.",
                    exception)
            };
        }

        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        if (!await attachments.HardDeleteForTransactionsAsync(
                transactions.Select(transaction => transaction.Id).ToArray(),
                cancellationToken))
        {
            return LifecycleResult(CategoryLifecycleOutcome.AttachmentStorageUnavailable);
        }

        context.FinancialTransactions.RemoveRange(transactions);
        context.RecurringTransactions.RemoveRange(recurringTransactions);
        context.Categories.RemoveRange(subtree);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return LifecycleResult(CategoryLifecycleOutcome.Succeeded, category.PublicId);
    }

    private async Task<bool> ParentChainHasCycleAsync(
        long userId,
        long parentId,
        CancellationToken cancellationToken)
    {
        var parents = await context.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId)
            .ToDictionaryAsync(
                category => category.Id,
                category => category.ParentId,
                cancellationToken);
        var visited = new HashSet<long>();
        long? current = parentId;

        while (current.HasValue)
        {
            if (!visited.Add(current.Value))
            {
                return true;
            }

            current = parents.GetValueOrDefault(current.Value);
        }

        return false;
    }

    private async Task<bool> ParentAssignmentHasCycleAsync(
        long userId,
        long categoryId,
        long parentId,
        CancellationToken cancellationToken)
    {
        var parents = await context.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId)
            .ToDictionaryAsync(
                category => category.Id,
                category => category.ParentId,
                cancellationToken);
        var visited = new HashSet<long>();
        long? current = parentId;

        while (current.HasValue)
        {
            if (current.Value == categoryId || !visited.Add(current.Value))
            {
                return true;
            }

            current = parents.GetValueOrDefault(current.Value);
        }

        return false;
    }

    private static CategoryCreationResult Result(CategoryCreationOutcome outcome) =>
        new(null, outcome);

    private static CategoryUpdateResult UpdateResult(CategoryUpdateOutcome outcome) =>
        new(null, outcome);

    private static CategoryTransactionReassignmentResult ReassignmentResult(
        CategoryTransactionReassignmentOutcome outcome) => new(0, outcome);

    private Task<Category?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.Categories
        .SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id,
            cancellationToken);

    private async Task<IReadOnlyCollection<Category>> CategorySubtreeAsync(
        Category root,
        CancellationToken cancellationToken)
    {
        var categories = await context.Categories
            .Where(item => item.UserId == root.UserId)
            .ToListAsync(cancellationToken);
        var children = categories.ToLookup(item => item.ParentId);
        var subtree = new List<Category>();
        var visited = new HashSet<long>();
        var pending = new Queue<Category>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var category))
        {
            if (!visited.Add(category.Id))
            {
                continue;
            }

            subtree.Add(category);
            foreach (var child in children[category.Id])
            {
                pending.Enqueue(child);
            }
        }

        return subtree;
    }

    private static CategoryLifecycleResult LifecycleResult(
        CategoryLifecycleOutcome outcome,
        Guid? id = null,
        int liveTransactionCount = 0) => new(id, outcome, liveTransactionCount);

    private sealed record CategoryIdentity(long Id, long UserId, Guid PublicId);
    private sealed record CategoryParent(long Id, long? ParentId);
}
