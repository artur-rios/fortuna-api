using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Lifecycle;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task GivenFinancialAccountMigration_WhenInspected_ThenMoneyAndLiveNameRulesAreDurable()
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT numeric_precision, numeric_scale,
                   (SELECT indexdef
                    FROM pg_indexes
                    WHERE schemaname = 'fortuna'
                      AND tablename = 'financial_account'
                      AND indexname = 'ux_financial_account_user_normalized_name_live')
            FROM information_schema.columns
            WHERE table_schema = 'fortuna'
              AND table_name = 'financial_account'
              AND column_name = 'opening_balance';
            """;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(19, reader.GetInt32(0));
        Assert.Equal(4, reader.GetInt32(1));
        Assert.Contains("WHERE (NOT is_deleted)", reader.GetString(2), StringComparison.Ordinal);
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
    public async Task GivenMappedLifecycleRecord_WhenSoftDeletedAndRestored_ThenStatePersistsAndLiveQueryExcludesIt()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var profile = new UserProfile(Guid.NewGuid(), "Lifecycle User", currency, DateTimeOffset.UtcNow);
        context.UserProfiles.Add(profile);
        await context.SaveChangesAsync();
        var deletedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var deletion = profile.SoftDelete(deletedAt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var deleted = await context.UserProfiles.SingleAsync(item => item.PublicId == profile.PublicId);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(deletion.CascadeId, deleted.DeletionCascadeId);
        Assert.False(await context.UserProfiles.WhereLive().AnyAsync(item => item.PublicId == profile.PublicId));

        var cascadeId = deleted.Restore(DateTimeOffset.UtcNow.AddMinutes(2));
        await context.SaveChangesAsync();
        Assert.Equal(deletion.CascadeId, cascadeId);
        Assert.True(await context.UserProfiles.WhereLive().AnyAsync(item => item.PublicId == profile.PublicId));
    }

    [FunctionalFact]
    public async Task GivenExistingAuditRows_WhenLifecycleMigrationRuns_ThenActorPublicIdsArePreserved()
    {
        var databaseName = $"fortuna_migration_{Guid.NewGuid():N}";
        var connectionBuilder = new NpgsqlConnectionStringBuilder(database.GetConnectionString());
        await using (var adminConnection = new NpgsqlConnection(connectionBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var createDatabase = adminConnection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await createDatabase.ExecuteNonQueryAsync();
        }

        connectionBuilder.Database = databaseName;
        await using (var previousContext = CreateContext(connectionBuilder.ConnectionString))
        {
            await previousContext.GetService<IMigrator>().MigrateAsync(
                "20260904012237_AddManualExchangeRateAudit");
        }

        var actorId = Guid.NewGuid();
        await using (var previousConnection = new NpgsqlConnection(connectionBuilder.ConnectionString))
        {
            await previousConnection.OpenAsync();
            await using var insert = previousConnection.CreateCommand();
            insert.CommandText = """
                INSERT INTO fortuna.currency (code, name, minor_unit_digits)
                VALUES ('BRL', 'Brazilian Real', 2);

                INSERT INTO fortuna."user" (
                    public_id, external_subject, display_name, display_currency_id,
                    is_deleted, created_at, updated_at)
                VALUES (
                    @actor_id, CAST(@actor_id AS text), 'Migration Actor',
                    (SELECT id FROM fortuna.currency WHERE code = 'BRL'),
                    false, now(), now());

                INSERT INTO fortuna.audit_entry (user_id, operation, outcome, occurred_at)
                SELECT id, 'ExistingWriteCommand', 1, now()
                FROM fortuna."user"
                WHERE public_id = @actor_id;
                """;
            insert.Parameters.AddWithValue("actor_id", actorId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var currentContext = CreateContext(connectionBuilder.ConnectionString))
        {
            await currentContext.Database.MigrateAsync();
        }

        await using var currentConnection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await currentConnection.OpenAsync();
        await using var select = currentConnection.CreateCommand();
        select.CommandText = """
            SELECT actor_user_id
            FROM fortuna.audit_entry
            WHERE operation = 'ExistingWriteCommand';
            """;

        Assert.Equal(actorId, (Guid?)await select.ExecuteScalarAsync());
    }

    [FunctionalFact]
    public async Task GivenAuditedSoftDeletedRecord_WhenHardDeleted_ThenAuditEntrySurvivesWithoutForeignKey()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var actor = new UserProfile(Guid.NewGuid(), "Audit Actor", currency, DateTimeOffset.UtcNow);
        var target = new UserProfile(Guid.NewGuid(), "Hard Delete Target", currency, DateTimeOffset.UtcNow);
        context.UserProfiles.AddRange(actor, target);
        await context.SaveChangesAsync();
        var audit = new AuditEntry(
            actor.PublicId,
            "DeleteRecordCommand",
            "UserProfile",
            target.PublicId,
            AuditOutcome.Succeeded,
            null,
            DateTimeOffset.UtcNow);
        context.AuditEntries.Add(audit);
        await context.SaveChangesAsync();
        var actorId = actor.Id;
        var targetId = target.PublicId;
        var auditId = audit.Id;
        context.ChangeTracker.Clear();

        var persistedTarget = await context.UserProfiles.SingleAsync(item => item.PublicId == targetId);
        persistedTarget.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        persistedTarget = await context.UserProfiles.SingleAsync(item => item.PublicId == targetId);
        var retainedBeforeDelete = await context.AuditEntries
            .AsNoTracking()
            .SingleAsync(item => item.Id == auditId);
        Assert.Equal(actor.PublicId, retainedBeforeDelete.ActorUserId);
        Assert.NotEqual(actorId, persistedTarget.Id);
        persistedTarget.EnsureHardDeletionAllowed();
        context.UserProfiles.Remove(persistedTarget);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var retainedAudit = await context.AuditEntries.SingleAsync(item => item.Id == auditId);
        Assert.Equal(actor.PublicId, retainedAudit.ActorUserId);
        Assert.Equal(targetId, retainedAudit.EntityPublicId);
        Assert.Equal("DeleteRecordCommand", retainedAudit.Operation);
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

    [FunctionalFact]
    public async Task GivenManualAndPublishedRateOnSameDate_WhenRead_ThenManualRateTakesPrecedence()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currencies = await context.Currencies
            .Where(currency => currency.Code == "USD" || currency.Code == "EUR")
            .ToDictionaryAsync(currency => currency.Code);
        var date = new DateOnly(2030, 1, 1);
        context.ExchangeRates.AddRange(
            new ExchangeRate(currencies["USD"].Id, currencies["EUR"].Id, 0.8m, date, ExchangeRateSource.Published),
            new ExchangeRate(currencies["USD"].Id, currencies["EUR"].Id, 0.9m, date, ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
        var store = new EfExchangeRateStore(context);

        var rate = await store.FindApplicableAsync("USD", "EUR", date, CancellationToken.None);

        Assert.NotNull(rate);
        Assert.Equal(0.9m, rate.Rate);
        Assert.Equal(date, rate.RateDate);
        Assert.Equal(ExchangeRateSource.Manual, rate.Source);
    }

    [FunctionalFact]
    public async Task GivenNoRateOnFigureDate_WhenRead_ThenLatestPriorRateIsReturned()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currencies = await context.Currencies
            .Where(currency => currency.Code == "EUR" || currency.Code == "JPY")
            .ToDictionaryAsync(currency => currency.Code);
        var olderDate = new DateOnly(2029, 12, 1);
        var latestPriorDate = new DateOnly(2030, 1, 2);
        context.ExchangeRates.AddRange(
            new ExchangeRate(currencies["EUR"].Id, currencies["JPY"].Id, 150m, olderDate, ExchangeRateSource.Published),
            new ExchangeRate(currencies["EUR"].Id, currencies["JPY"].Id, 160m, latestPriorDate, ExchangeRateSource.Published));
        await context.SaveChangesAsync();
        var store = new EfExchangeRateStore(context);

        var rate = await store.FindApplicableAsync(
            "EUR",
            "JPY",
            latestPriorDate.AddDays(3),
            CancellationToken.None);

        Assert.NotNull(rate);
        Assert.Equal(160m, rate.Rate);
        Assert.Equal(latestPriorDate, rate.RateDate);
    }

    [FunctionalFact]
    public async Task GivenOnlyFutureRate_WhenRead_ThenNoApplicableRateIsReturned()
    {
        await using var context = CreateContext();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        var currencies = await context.Currencies
            .Where(currency => currency.Code == "GBP" || currency.Code == "CAD")
            .ToDictionaryAsync(currency => currency.Code);
        context.ExchangeRates.Add(new ExchangeRate(
            currencies["GBP"].Id,
            currencies["CAD"].Id,
            1.8m,
            new DateOnly(2031, 1, 1),
            ExchangeRateSource.Published));
        await context.SaveChangesAsync();
        var store = new EfExchangeRateStore(context);

        var rate = await store.FindApplicableAsync(
            "GBP",
            "CAD",
            new DateOnly(2030, 12, 31),
            CancellationToken.None);

        Assert.Null(rate);
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
        return CreateContext(database.GetConnectionString());
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
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
