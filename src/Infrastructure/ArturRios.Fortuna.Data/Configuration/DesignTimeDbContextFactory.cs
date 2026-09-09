using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Configuration;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionVariable = "FORTUNA_DATA_CONNECTIONSTRING";
    private const string ProviderVariable = "FORTUNA_DATA_DATABASETYPE";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Environment variable '{ConnectionVariable}' is required by the EF Core tools.");
        }

        var provider = Environment.GetEnvironmentVariable(ProviderVariable) ?? DatabaseProvider.PostgreSql;
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, provider, connection);
        var options = builder.Options;
        return new AppDbContext(options, NullLoggerFactory.Instance, DatabaseDiagnosticsOptions.Disabled);
    }
}
