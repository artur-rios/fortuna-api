using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Shared.Classification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Classification;

public sealed class EfCategoryStore(AppDbContext context)
    : ICategoryStore, ICategoryReader, ICategoryUpdater
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
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: RootSiblingNameIndex or NestedSiblingNameIndex
            })
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
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: RootSiblingNameIndex or NestedSiblingNameIndex
            })
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
}
