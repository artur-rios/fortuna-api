using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class DatabaseFoundationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenEmptyDatabase_WhenMigrated_ThenFortunaSchemaAndFoundationTablesExist()
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select count(*)
            from information_schema.tables
            where table_schema = 'fortuna'
              and table_name in (
                'currency', 'exchange_rate', 'background_job', 'user', 'local_account', 'recovery_code',
                'audit_entry');
            """;

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(7, count);
    }

    [FunctionalFact]
    public async Task GivenInitialMigration_WhenRateColumnIsInspected_ThenItUsesExactConfiguredPrecision()
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select data_type, numeric_precision, numeric_scale
            from information_schema.columns
            where table_schema = 'fortuna'
              and table_name = 'exchange_rate'
              and column_name = 'rate';
            """;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("numeric", reader.GetString(0));
        Assert.Equal(19, reader.GetInt32(1));
        Assert.Equal(8, reader.GetInt32(2));
    }

    [FunctionalFact]
    public async Task GivenCurrencySeedRunsTwice_WhenSaved_ThenReferenceSetIsNotDuplicated()
    {
        await using var context = CreateContext();
        var seeder = new DatabaseSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.True(await context.Currencies.CountAsync(CancellationToken.None) >= 170);
        Assert.Equal(1, await context.Currencies.CountAsync(x => x.Code == "BRL", CancellationToken.None));
        Assert.Equal(2, await context.Currencies.Where(x => x.Code == "BRL").Select(x => x.MinorUnitDigits).SingleAsync(CancellationToken.None));
        Assert.Equal(0, await context.Currencies.Where(x => x.Code == "JPY").Select(x => x.MinorUnitDigits).SingleAsync(CancellationToken.None));
    }

    [FunctionalFact]
    public async Task GivenDuplicateIdempotencyKey_WhenJobIsCreatedTwice_ThenOnlyOneDurableJobExists()
    {
        await using var context = CreateContext();
        var store = new EfBackgroundJobStore(context);

        var first = await store.CreateAsync("import", "{}", "same-request", null, CancellationToken.None);
        var second = await store.CreateAsync("import", "{}", "same-request", null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await context.BackgroundJobs.CountAsync(x => x.IdempotencyKey == "same-request", CancellationToken.None));
    }

    [FunctionalFact]
    public async Task GivenRunningAndPendingJobs_WhenRecovering_ThenBothAreReturnedPending()
    {
        await using var context = CreateContext();
        var running = BackgroundJob.Create("import", "{}", Guid.NewGuid().ToString(), null, DateTimeOffset.UtcNow);
        running.Start(DateTimeOffset.UtcNow);
        var pending = BackgroundJob.Create("export", "{}", Guid.NewGuid().ToString(), null, DateTimeOffset.UtcNow);
        context.BackgroundJobs.AddRange(running, pending);
        await context.SaveChangesAsync(CancellationToken.None);
        var store = new EfBackgroundJobStore(context);

        var recovered = await store.RecoverAsync(CancellationToken.None);

        Assert.Contains(recovered, x => x.Id == running.Id && x.State == BackgroundJobState.Pending);
        Assert.Contains(recovered, x => x.Id == pending.Id && x.State == BackgroundJobState.Pending);
    }

    [FunctionalFact]
    public async Task GivenConcurrentLocalAccountCreations_WhenPersisted_ThenExactlyOneWins()
    {
        await using var seedContext = CreateContext();
        await new DatabaseSeeder(seedContext).SeedAsync(CancellationToken.None);
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var options = new LocalAccountOptions(true, 1, "BRL", "pt-BR");
        var firstStore = new EfLocalAccountStore(firstContext, options);
        var secondStore = new EfLocalAccountStore(secondContext, options);

        var results = await Task.WhenAll(
            firstStore.CreateAsync(LocalCreation("First User", 1), CancellationToken.None),
            secondStore.CreateAsync(LocalCreation("Second User", 2), CancellationToken.None));

        Assert.Equal(1, results.Count(result => result.Account is not null));
        Assert.Equal(1, results.Count(result => result.AlreadyExists));
        await using var assertionContext = CreateContext();
        Assert.Equal(1, await assertionContext.LocalAccounts.CountAsync());
        Assert.Equal(1, await assertionContext.UserProfiles.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenPublishedAndManualRates_WhenPublishedRatesAreSynchronized_ThenOnlyPublishedRowsChange()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currencies = await context.Currencies
            .Where(currency => currency.Code == "BRL" || currency.Code == "USD")
            .ToDictionaryAsync(currency => currency.Code);
        var date = new DateOnly(2026, 9, 1);
        context.ExchangeRates.AddRange(
            new ExchangeRate(
                currencies["USD"].Id,
                currencies["BRL"].Id,
                5.1m,
                date,
                ExchangeRateSource.Published),
            new ExchangeRate(
                currencies["USD"].Id,
                currencies["BRL"].Id,
                5.3m,
                date,
                ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
        var store = new EfExchangeRateStore(context);
        PublishedRateCandidate[] candidates =
        [
            new("USD", "BRL", 5.2m, date),
            new("BRL", "USD", 1m / 5.2m, date)
        ];

        var changed = await store.UpsertPublishedAsync(candidates, CancellationToken.None);
        var unchanged = await store.UpsertPublishedAsync(candidates, CancellationToken.None);

        Assert.Equal(new PublishedRateUpsertResult(2, 0), changed);
        Assert.Equal(new PublishedRateUpsertResult(0, 2), unchanged);
        Assert.Equal(5.2m, await context.ExchangeRates
            .Where(rate => rate.Source == ExchangeRateSource.Published &&
                rate.BaseCurrencyId == currencies["USD"].Id)
            .Select(rate => rate.Rate)
            .SingleAsync());
        Assert.Equal(5.3m, await context.ExchangeRates
            .Where(rate => rate.Source == ExchangeRateSource.Manual)
            .Select(rate => rate.Rate)
            .SingleAsync());
    }

    [FunctionalFact]
    public async Task GivenPublishedRate_WhenManualRateIsUpsertedTwice_ThenPublishedRemainsAndManualIsReplaced()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currencies = await context.Currencies
            .Where(currency => currency.Code == "BRL" || currency.Code == "USD")
            .ToDictionaryAsync(currency => currency.Code);
        var date = new DateOnly(2026, 9, 4);
        context.ExchangeRates.Add(new ExchangeRate(
            currencies["USD"].Id,
            currencies["BRL"].Id,
            5.1m,
            date,
            ExchangeRateSource.Published));
        await context.SaveChangesAsync();
        var store = new EfExchangeRateStore(context);

        var created = await store.UpsertManualAsync(
            new ManualRateCandidate("USD", "BRL", 5.25m, date),
            CancellationToken.None);
        var replaced = await store.UpsertManualAsync(
            new ManualRateCandidate("USD", "BRL", 5.4m, date),
            CancellationToken.None);

        Assert.False(created.ReplacedExisting);
        Assert.True(replaced.ReplacedExisting);
        Assert.Equal(5.4m, replaced.Rate);
        Assert.Equal(5.1m, await context.ExchangeRates
            .Where(rate => rate.Source == ExchangeRateSource.Published && rate.RateDate == date)
            .Select(rate => rate.Rate)
            .SingleAsync());
        Assert.Equal(5.4m, await context.ExchangeRates
            .Where(rate => rate.Source == ExchangeRateSource.Manual && rate.RateDate == date)
            .Select(rate => rate.Rate)
            .SingleAsync());
    }

    [FunctionalTheory]
    [InlineData("update fortuna.audit_entry set operation = operation")]
    [InlineData("delete from fortuna.audit_entry where false")]
    [InlineData("truncate table fortuna.audit_entry")]
    public async Task GivenAuditEntryTable_WhenMutationIsAttempted_ThenDatabaseRefusesIt(string sql)
    {
        await using var context = CreateContext();
        context.AuditEntries.Add(new AuditEntry(
            null,
            "RecordManualExchangeRateCommand",
            null,
            null,
            AuditOutcome.Succeeded,
            null,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
            await command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        return new AppDbContext(options, NullLoggerFactory.Instance, DatabaseDiagnosticsOptions.Disabled);
    }

    private static LocalAccountCreation LocalCreation(string name, byte marker) => new(
        name,
        [marker, 10],
        [marker, 20],
        LocalAccountStorageMode.InMemory,
        [[marker, 30]],
        DateTimeOffset.UtcNow);
}
