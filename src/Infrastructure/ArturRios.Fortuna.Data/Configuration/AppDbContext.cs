using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Transactions;
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
    public DbSet<LocalAccount> LocalAccounts => Set<LocalAccount>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<InstallmentPlan> InstallmentPlans => Set<InstallmentPlan>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<CreditCardStatement> CreditCardStatements => Set<CreditCardStatement>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<InvestmentMovement> InvestmentMovements => Set<InvestmentMovement>();
    public DbSet<InvestmentValuation> InvestmentValuations => Set<InvestmentValuation>();

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
