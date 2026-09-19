using ArturRios.Fortuna.Data.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.Data.Tests;

/// <summary>
/// A migrated database seeded once with <see cref="ReportingScenario"/> and shared by every
/// read-only reporting test of one provider.
/// </summary>
public abstract class ReportingDatabaseFixture : IAsyncLifetime
{
    private ReportingScenario? scenario;

    internal ReportingScenario Scenario => scenario!;

    public abstract AppDbContext CreateContext();

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync();
        await using (var migration = CreateContext())
        {
            await migration.Database.MigrateAsync();
        }

        await using var context = CreateContext();
        scenario = await ReportingScenario.SeedAsync(context);
    }

    public abstract Task DisposeAsync();

    protected abstract Task CreateDatabaseAsync();

    protected static AppDbContext CreateContext(string provider, string connection)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, provider, connection);

        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }
}

public sealed class PostgreSqlReportingFixture : ReportingDatabaseFixture
{
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public override AppDbContext CreateContext() =>
        CreateContext(DatabaseProvider.PostgreSql, database.GetConnectionString());

    public override async Task DisposeAsync() => await database.DisposeAsync();

    protected override Task CreateDatabaseAsync() => database.StartAsync();
}

public sealed class SqliteReportingFixture : ReportingDatabaseFixture
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        $"fortuna-reporting-{Guid.NewGuid():N}.db");

    public override AppDbContext CreateContext() =>
        CreateContext(DatabaseProvider.SQLite, path);

    public override Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);

        return Task.CompletedTask;
    }

    protected override Task CreateDatabaseAsync() => Task.CompletedTask;
}
