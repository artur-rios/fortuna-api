using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Configuration;

internal static class DatabaseLock
{
    public static async Task AcquireAsync(
        AppDbContext context,
        long lockId,
        CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockId})",
                cancellationToken);
            return;
        }

        if (!context.Database.IsSqlite())
        {
            throw new NotSupportedException(
                $"Database provider '{context.Database.ProviderName}' does not support Fortuna's locking strategy.");
        }

        // SQLite serializes writes for the active transaction, so no separate advisory lock is needed.
    }
}
