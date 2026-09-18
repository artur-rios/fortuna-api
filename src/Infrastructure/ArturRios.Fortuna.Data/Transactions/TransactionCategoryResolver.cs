using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Data.Transactions;

internal static class TransactionCategoryResolver
{
    public const string Transfers = "Transfers";

    public static Task<Category> GetOrCreateAsync(
        AppDbContext context,
        UserProfile user,
        string name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken) => ClassificationResolver.GetOrCreateRootCategoryAsync(
        context,
        user,
        name,
        createdAt,
        cancellationToken);
}
