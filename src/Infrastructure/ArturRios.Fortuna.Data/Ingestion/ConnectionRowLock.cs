using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Ingestion;

internal static class ConnectionRowLock
{
    public static Task<Connection?> FindAsync(
        AppDbContext context,
        long connectionId,
        CancellationToken cancellationToken) => context.Connections
        .FromSqlInterpolated(
            $"SELECT * FROM fortuna.connection WHERE id = {connectionId} FOR UPDATE")
        .SingleOrDefaultAsync(cancellationToken);

    public static async Task<Connection?> FindOwnedAsync(
        AppDbContext context,
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var ownerId = await context.UserProfiles
            .AsNoTracking()
            .Where(user => user.PublicId == userId)
            .Select(user => (long?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return ownerId is null
            ? null
            : await context.Connections
                .FromSqlInterpolated(
                    $"SELECT * FROM fortuna.connection WHERE public_id = {connectionId} AND user_id = {ownerId.Value} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
    }
}
