using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Data.Configuration;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ILoggerFactory loggerFactory,
    DatabaseDiagnosticsOptions diagnostics) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "fortuna";
    private static readonly ValueConverter<DateTimeOffset, long> SqliteDateTimeOffsetConverter = new(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));

    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<DataExport> DataExports => Set<DataExport>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<LocalAccount> LocalAccounts => Set<LocalAccount>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AuditSubject> AuditSubjects => Set<AuditSubject>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<InstallmentPlan> InstallmentPlans => Set<InstallmentPlan>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<CreditCardStatement> CreditCardStatements => Set<CreditCardStatement>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<InvestmentMovement> InvestmentMovements => Set<InvestmentMovement>();
    public DbSet<InvestmentValuation> InvestmentValuations => Set<InvestmentValuation>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportedRecord> ImportedRecords => Set<ImportedRecord>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<ConnectionResource> ConnectionResources => Set<ConnectionResource>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
        if (!Database.IsSqlite())
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (Database.IsSqlite())
        {
            ConfigureSqliteModel(modelBuilder);
        }
    }

    private static void ConfigureSqliteModel(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model
                     .GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
            {
                property.SetColumnType("TEXT");
            }
            else if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(SqliteDateTimeOffsetConverter);
                property.SetColumnType("INTEGER");
            }
            else if (string.Equals(property.GetColumnType(), "jsonb", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType("TEXT");
            }
        }

        modelBuilder.Entity<CreditCard>().ToTable("credit_card", table => table.HasCheckConstraint(
            "ck_credit_card_last_four_digits",
            "last_four_digits IS NULL OR (length(last_four_digits) = 4 AND last_four_digits NOT GLOB '*[^0-9]*')"));
    }
}
