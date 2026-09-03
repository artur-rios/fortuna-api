using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Jobs;
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
              and table_name in ('currency', 'exchange_rate', 'background_job');
            """;

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(3, count);
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
}
