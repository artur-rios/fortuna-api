using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Configuration;

internal static class RowLock
{
    // Takes PostgreSQL row locks (SELECT ... FOR UPDATE) on the given rows until the active
    // transaction ends, in key order so concurrent callers cannot deadlock on each other.
    // SQLite needs no row lock: its transactions start with BEGIN IMMEDIATE, which already
    // serializes every writer for the whole transaction.
    public static async Task LockForUpdateAsync<TEntity>(
        AppDbContext context,
        IEnumerable<long> ids,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keys = ids.Where(id => id > 0).Distinct().Order().ToArray();
        if (keys.Length == 0 || !context.Database.IsNpgsql())
        {
            return;
        }

        var table = context.Model.FindEntityType(typeof(TEntity))!
            .GetTableMappings()
            .First()
            .Table;
        var name = table.Schema is null
            ? $"\"{table.Name}\""
            : $"\"{table.Schema}\".\"{table.Name}\"";
#pragma warning disable EF1002 // The table name comes from the model, never from input.
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {name} WHERE id = ANY({{0}}) ORDER BY id FOR UPDATE",
            [keys],
            cancellationToken);
#pragma warning restore EF1002
    }
}
