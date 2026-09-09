using ArturRios.Fortuna.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Sqlite.Migrations;

public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("FORTUNA_DATA_CONNECTIONSTRING");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "Environment variable 'FORTUNA_DATA_CONNECTIONSTRING' is required by the EF Core tools.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.SQLite, connection);
        return new AppDbContext(
            options.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }
}
