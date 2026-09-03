using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Data.Configuration;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ILoggerFactory loggerFactory,
    DatabaseDiagnosticsOptions diagnostics) : DbContext(options)
{
    public const string Schema = "fortuna";

    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseLoggerFactory(loggerFactory)
            .UseSnakeCaseNamingConvention()
            .EnableDetailedErrors(diagnostics.DetailedErrors)
            .EnableSensitiveDataLogging(diagnostics.SensitiveDataLogging);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
