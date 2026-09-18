using ArturRios.Fortuna.Data.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private SqliteTestDatabase(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var database = new SqliteTestDatabase(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"fortuna-{Guid.NewGuid():N}.db"));
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        return database;
    }

    public AppDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, Path);

        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(Path);

        return ValueTask.CompletedTask;
    }
}
