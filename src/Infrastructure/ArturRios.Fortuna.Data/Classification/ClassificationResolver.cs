using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Classification;

// Get-or-create for the classification records that transactions reference by name. Callers
// must run inside a database transaction: a missing record is saved immediately, so a
// concurrent creation of the same live name surfaces as a unique violation on the live-name
// index, after which the record the other writer created is read back and reused.
internal static class ClassificationResolver
{
    private const int CreationAttempts = 3;

    public static async Task<Counterparty?> GetOrCreateCounterpartyAsync(
        AppDbContext context,
        UserProfile user,
        string? name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedName = Normalize(name);

        return await GetOrCreateAsync(
            context,
            () => context.Counterparties.SingleOrDefaultAsync(item =>
                item.UserId == user.Id &&
                item.NormalizedName == normalizedName &&
                !item.IsDeleted,
                cancellationToken),
            () => new Counterparty(user, name, createdAt),
            CounterpartyMap.LiveNameIndex,
            cancellationToken);
    }

    public static async Task<IReadOnlyCollection<Tag>> GetOrCreateTagsAsync(
        AppDbContext context,
        UserProfile user,
        IReadOnlyCollection<string> names,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var requested = names
            .Select(name => name.Trim())
            .DistinctBy(Normalize)
            .ToArray();
        var tags = new List<Tag>(requested.Length);
        foreach (var name in requested)
        {
            var normalizedName = Normalize(name);
            tags.Add(await GetOrCreateAsync(
                context,
                () => context.Tags.SingleOrDefaultAsync(item =>
                    item.UserId == user.Id &&
                    item.NormalizedName == normalizedName &&
                    !item.IsDeleted,
                    cancellationToken),
                () => new Tag(user, name, createdAt),
                TagMap.LiveNameIndex,
                cancellationToken));
        }

        return tags;
    }

    public static Task<Category> GetOrCreateRootCategoryAsync(
        AppDbContext context,
        UserProfile user,
        string name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var normalizedName = Normalize(name);

        return GetOrCreateAsync(
            context,
            () => context.Categories.SingleOrDefaultAsync(item =>
                item.UserId == user.Id &&
                item.ParentId == null &&
                item.NormalizedName == normalizedName &&
                !item.IsDeleted,
                cancellationToken),
            () => new Category(user, name, createdAt),
            CategoryMap.RootNameIndex,
            cancellationToken);
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static async Task<TEntity> GetOrCreateAsync<TEntity>(
        AppDbContext context,
        Func<Task<TEntity?>> find,
        Func<TEntity> create,
        string liveNameIndex,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        for (var attempt = 1; ; attempt++)
        {
            var existing = await find();
            if (existing is not null)
            {
                return existing;
            }

            var created = create();
            context.Add(created);
            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return created;
            }
            catch (DbUpdateException exception) when (
                attempt < CreationAttempts &&
                DatabaseException.IsUniqueViolation(exception, liveNameIndex))
            {
                context.Entry(created).State = EntityState.Detached;
            }
        }
    }
}
