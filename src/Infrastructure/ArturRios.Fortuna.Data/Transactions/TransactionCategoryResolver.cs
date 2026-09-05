using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

internal static class TransactionCategoryResolver
{
    public const string Transfers = "Transfers";

    public static async Task<Category> GetOrCreateAsync(
        AppDbContext context,
        UserProfile user,
        string name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToUpperInvariant();
        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.UserId == user.Id &&
            item.ParentId == null &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken);
        if (category is not null)
        {
            return category;
        }

        category = new Category(user, name, createdAt);
        context.Categories.Add(category);
        return category;
    }
}
