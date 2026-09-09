using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Configuration;

public static class DatabaseProvider
{
    public const string PostgreSql = "PostgreSql";
    public const string SQLite = "SQLite";
    public const string SQLiteMigrationsAssembly = "ArturRios.Fortuna.Data.Sqlite.Migrations";

    public static bool IsSupported(string value) =>
        string.Equals(value, PostgreSql, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, SQLite, StringComparison.OrdinalIgnoreCase);

    public static void Configure(
        DbContextOptionsBuilder options,
        string provider,
        string connection)
    {
        if (string.Equals(provider, PostgreSql, StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(
                connection,
                postgres => postgres.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    AppDbContext.Schema));
            return;
        }

        if (string.Equals(provider, SQLite, StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = connection.Contains('=', StringComparison.Ordinal)
                ? connection
                : $"Data Source={connection}";
            options.UseSqlite(
                connectionString,
                sqlite => sqlite
                    .MigrationsAssembly(SQLiteMigrationsAssembly)
                    .MigrationsHistoryTable("__ef_migrations_history"));
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported database provider '{provider}'. Expected '{PostgreSql}' or '{SQLite}'.");
    }
}
