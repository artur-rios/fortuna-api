using ArturRios.Fortuna.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class PostgreSqlReportingReaderTests : ReportingReaderTests, IAsyncLifetime
{
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await SeedAsync();
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    protected override AppDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.PostgreSql, database.GetConnectionString());

        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }
}
