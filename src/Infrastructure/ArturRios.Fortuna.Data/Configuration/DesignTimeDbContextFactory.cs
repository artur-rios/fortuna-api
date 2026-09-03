using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Configuration;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionVariable = "FORTUNA_DATA_CONNECTIONSTRING";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Environment variable '{ConnectionVariable}' is required by the EF Core tools.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
            .Options;
        return new AppDbContext(options, NullLoggerFactory.Instance, DatabaseDiagnosticsOptions.Disabled);
    }
}
