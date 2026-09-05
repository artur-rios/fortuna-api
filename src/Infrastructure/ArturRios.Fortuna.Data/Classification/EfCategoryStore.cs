using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Shared.Classification;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Classification;

public sealed class EfCategoryStore(AppDbContext context) : ICategoryStore
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

    private static CategoryCreationResult Result(CategoryCreationOutcome outcome) =>
        new(null, outcome);
}
