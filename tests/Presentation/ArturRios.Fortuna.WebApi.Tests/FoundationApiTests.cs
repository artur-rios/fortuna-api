using System.Net;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class FoundationApiTests
{
    [UnitFact]
    public void GivenRequiredSettingMissing_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values.Remove("FORTUNA_DATA_CONNECTIONSTRING");

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_DATA_CONNECTIONSTRING", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenValidFilesystemSettings_WhenConfigurationLoads_ThenDefaultsAndValuesAreApplied()
    {
        var values = ValidSettings();
        values.Remove("FORTUNA_JOB_QUEUE_CAPACITY");

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("PostgreSql", options.DataDatabaseType);
        Assert.Equal("Filesystem", options.StorageProvider);
        Assert.NotNull(options.StoragePath);
        Assert.Equal(256, options.JobQueueCapacity);
        Assert.Equal(100, options.PageSizeMaximum);
        Assert.False(options.RunMigrations);
        Assert.Equal("BRL", options.DefaultDisplayCurrency);
        Assert.Equal("pt-BR", options.Locale);
        Assert.False(options.LocalAuthEnabled);
        Assert.Equal(10, options.LocalAuthRecoveryCodeCount);
        Assert.Equal(0.01m, options.ReconciliationAmountTolerance);
        Assert.Equal(1, options.ReconciliationDateToleranceDays);
    }

    [UnitFact]
    public void GivenConfiguredReconciliationTolerances_WhenConfigurationLoads_ThenValuesAreApplied()
    {
        var values = ValidSettings();
        values["FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE"] = "2.50";
        values["FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS"] = "3";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal(2.50m, options.ReconciliationAmountTolerance);
        Assert.Equal(3, options.ReconciliationDateToleranceDays);
    }

    [UnitTheory]
    [InlineData("amount", "-0.01")]
    [InlineData("amount", "invalid")]
    [InlineData("date", "-1")]
    [InlineData("date", "1.5")]
    public void GivenInvalidReconciliationTolerance_WhenConfigurationLoads_ThenStartupIsRejected(
        string field,
        string value)
    {
        var values = ValidSettings();
        var key = field == "amount"
            ? "FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE"
            : "FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS";
        values[key] = value;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenDefaultCurrencyIsLowercase_WhenConfigurationLoads_ThenItIsNormalized()
    {
        var values = ValidSettings();
        values["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "usd";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("USD", options.DefaultDisplayCurrency);
    }

    [UnitFact]
    public void GivenLocaleMissing_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values.Remove("FORTUNA_LOCALE");

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_LOCALE", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("not-a-locale")]
    [InlineData("pt")]
    public void GivenInvalidOrNeutralLocale_WhenConfigurationLoads_ThenStartupIsRejected(string locale)
    {
        var values = ValidSettings();
        values["FORTUNA_LOCALE"] = locale;

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("specific locale", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("US")]
    [InlineData("123")]
    public void GivenInvalidDefaultCurrency_WhenConfigurationLoads_ThenStartupIsRejected(string currency)
    {
        var values = ValidSettings();
        values["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = currency;

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("ISO 4217", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenCompleteS3Settings_WhenConfigurationLoads_ThenS3ProviderIsAccepted()
    {
        var values = ValidSettings();
        values["FORTUNA_STORAGE_PROVIDER"] = "S3";
        values["FORTUNA_STORAGE_S3_ENDPOINT"] = "https://s3.example.test";
        values["FORTUNA_STORAGE_S3_BUCKET"] = "receipts";
        values["FORTUNA_STORAGE_S3_ACCESS_KEY"] = "access";
        values["FORTUNA_STORAGE_S3_SECRET_KEY"] = "secret";
        values["FORTUNA_RUN_MIGRATIONS"] = "true";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("S3", options.StorageProvider);
        Assert.Equal("receipts", options.StorageS3Bucket);
        Assert.True(options.RunMigrations);
    }

    [UnitFact]
    public void GivenUnsupportedDatabaseType_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_DATA_DATABASETYPE"] = "Sqlite";

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("PostgreSql", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenUnsupportedStorageProvider_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_STORAGE_PROVIDER"] = "database";

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("Filesystem", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidQueueCapacity_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_JOB_QUEUE_CAPACITY"] = value;

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("positive integer", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidMaximumPageSize_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_PAGE_SIZE_MAX"] = value;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_PAGE_SIZE_MAX", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInvalidMigrationFlag_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_RUN_MIGRATIONS"] = "sometimes";

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("true or false", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInvalidLocalAuthenticationFlag_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_LOCAL_AUTH_ENABLED"] = "sometimes";

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_LOCAL_AUTH_ENABLED", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidRecoveryCodeCount_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = value;

        var exception = Assert.Throws<InvalidOperationException>(
            () => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenConfiguredRateSource_WhenConfigurationLoads_ThenUrlScheduleAndCurrenciesAreValidated()
    {
        var values = ValidSettings();
        values["FORTUNA_RATES_SOURCE_BASE_URL"] = "https://rates.example.test/odata";
        values["FORTUNA_RATES_SYNC_CRON"] = "0 18 * * 1-5";
        values["FORTUNA_RATES_CURRENCIES"] = "brl, usd, eur,USD";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal(new Uri("https://rates.example.test/odata/"), options.RatesSourceBaseUri);
        Assert.Equal("0 18 * * 1-5", options.RatesSyncCron);
        Assert.Equal(["BRL", "USD", "EUR"], options.RatesCurrencies);
    }

    [UnitTheory]
    [InlineData("not-a-url", "0 18 * * 1-5", "BRL,USD")]
    [InlineData("https://rates.example.test", "invalid", "BRL,USD")]
    [InlineData("https://rates.example.test", "0 18 * * 1-5", "BRL")]
    public void GivenInvalidRateSourceSettings_WhenConfigurationLoads_ThenStartupIsRejected(
        string url,
        string cron,
        string currencies)
    {
        var values = ValidSettings();
        values["FORTUNA_RATES_SOURCE_BASE_URL"] = url;
        values["FORTUNA_RATES_SYNC_CRON"] = cron;
        values["FORTUNA_RATES_CURRENCIES"] = currencies;

        Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));
    }

    [UnitFact]
    public void GivenWeekdayCron_WhenMatchingUtcInstants_ThenOnlyScheduledMinutesMatch()
    {
        var schedule = CronSchedule.Parse("*/15 9-17 * * 1-5");

        Assert.True(schedule.Matches(DateTimeOffset.Parse("2026-09-04T09:30:00Z")));
        Assert.False(schedule.Matches(DateTimeOffset.Parse("2026-09-05T09:30:00Z")));
        Assert.False(schedule.Matches(DateTimeOffset.Parse("2026-09-04T09:31:00Z")));
    }

    [FunctionalFact]
    public async Task GivenRunningApi_WhenLivenessIsRequested_ThenSuccessDoesNotRequireAuthentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthcheck", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenDevelopmentApi_WhenOpenApiIsRequested_ThenDocumentIsServed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json", CancellationToken.None);
        var document = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/healthcheck", document);
        Assert.Contains("\"securitySchemes\"", document);
        Assert.Contains("\"Bearer\"", document);
    }

    [FunctionalFact]
    public void GivenAnyEnvironment_WhenDatabaseDiagnosticsAreConfigured_ThenSensitiveValuesAreNeverLogged()
    {
        using var factory = CreateFactory();

        var diagnostics = factory.Services.GetRequiredService<
            ArturRios.Fortuna.Data.Configuration.DatabaseDiagnosticsOptions>();

        Assert.False(diagnostics.SensitiveDataLogging);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
                services.RemoveAll<IHostedService>());
        });
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };
}
