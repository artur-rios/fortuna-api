using ArturRios.Fortuna.Data.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

internal static class SqliteTestDatabase
{
    public static string TemporaryPath(string prefix = "fortuna") =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");

    public static AppDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);

        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    public static void Delete(string path)
    {
        // Pooled connections keep the file open, which makes deletion fail on Windows.
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            SqliteConnection.ClearPool(connection);
        }

        File.Delete(path);
    }
}
