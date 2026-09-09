using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task GivenSqliteConfiguration_WhenMigrated_ThenDatabaseIsReadyWithoutCodeChanges()
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(path);

            await context.Database.MigrateAsync();

            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            Assert.Contains(
                await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_InitialSqlite", StringComparison.Ordinal));
            Assert.True(await context.Currencies.AnyAsync() is false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GivenExactMoneyValues_WhenStoredAndAggregated_ThenNoPrecisionIsLost()
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(path);
            await context.Database.MigrateAsync();
            var usd = new Currency("USD", "US Dollar", 2);
            var eur = new Currency("EUR", "Euro", 2);
            context.Currencies.AddRange(usd, eur);
            await context.SaveChangesAsync();
            context.ExchangeRates.AddRange(
                new ExchangeRate(usd.Id, eur.Id, 0.10000001m, new DateOnly(2026, 1, 1), ExchangeRateSource.Manual),
                new ExchangeRate(usd.Id, eur.Id, 0.20000002m, new DateOnly(2026, 1, 2), ExchangeRateSource.Manual));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var values = await context.ExchangeRates.OrderBy(rate => rate.RateDate).Select(rate => rate.Rate).ToArrayAsync();
            var total = await context.ExchangeRates.SumAsync(rate => rate.Rate);

            Assert.Equal([0.10000001m, 0.20000002m], values);
            Assert.Equal(0.30000003m, total);

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT typeof(rate) FROM exchange_rate LIMIT 1";
            Assert.Equal("text", await command.ExecuteScalarAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GivenSqliteModel_WhenMonetaryPropertiesAreInspected_ThenNoneUseFloatingPoint()
    {
        var path = TemporaryDatabasePath();
        try
        {
            using var context = CreateContext(path);

            var decimalProperties = context.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties())
                .Where(property => Nullable.GetUnderlyingType(property.ClrType) == typeof(decimal) ||
                                   property.ClrType == typeof(decimal))
                .ToArray();

            Assert.NotEmpty(decimalProperties);
            Assert.All(decimalProperties, property => Assert.Equal("TEXT", property.GetColumnType()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GivenTimestampedRows_WhenOrdered_ThenSqliteMatchesApplicationSemantics()
    {
        var path = TemporaryDatabasePath();
        try
        {
            await using var context = CreateContext(path);
            await context.Database.MigrateAsync();
            var earlier = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(-3));
            var later = earlier.AddTicks(1);
            context.BackgroundJobs.AddRange(
                BackgroundJob.Create("test", "{}", "later", null, later),
                BackgroundJob.Create("test", "{}", "earlier", null, earlier));
            await context.SaveChangesAsync();

            var ordered = await context.BackgroundJobs
                .OrderBy(job => job.CreatedAt)
                .Select(job => job.IdempotencyKey)
                .ToArrayAsync();

            Assert.Equal(["earlier", "later"], ordered);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);
        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"fortuna-{Guid.NewGuid():N}.db");
}
