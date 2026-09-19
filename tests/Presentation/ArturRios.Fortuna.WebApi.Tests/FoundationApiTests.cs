using System.Globalization;
using System.Net;
using ArturRios.Fortuna.WebApi.Configuration;
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

        var exception = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

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
        Assert.Equal(300, options.HealthJobMaximumPendingSeconds);
        Assert.Equal(100, options.PageSizeMaximum);
        Assert.Equal(366, options.ReportMaximumRangeDays);
        Assert.Equal(15, options.ReportKeyLifetimeMinutes);
        Assert.Equal(366, options.ProjectionMaximumHorizonDays);
        Assert.Equal(1000, options.ExportSynchronousThresholdRows);
        Assert.Equal(24, options.ExportRetentionHours);
        Assert.Equal(50, options.TransactionMaximumTags);
        Assert.Equal(10 * 1024 * 1024, options.UploadMaximumBytes);
        Assert.Equal(["application/pdf", "image/jpeg", "image/png"],
            options.UploadAllowedContentTypes);
        Assert.False(options.RunMigrations);
        Assert.Equal("BRL", options.DefaultDisplayCurrency);
        Assert.Equal("pt-BR", options.Locale);
        Assert.False(options.LocalAuthEnabled);
        Assert.Equal(10, options.LocalAuthRecoveryCodeCount);
        Assert.Equal("1.0", options.ConsentExternalDataProcessingVersion);
        Assert.Equal(0.01m, options.ReconciliationAmountTolerance);
        Assert.Equal(1, options.ReconciliationDateToleranceDays);
        Assert.Equal(9464, options.MetricsPort);
    }

    [UnitTheory]
    [InlineData("", 9464)]
    [InlineData("0", 0)]
    [InlineData("9100", 9100)]
    [InlineData("65535", 65535)]
    public void GivenMetricsPortSetting_WhenConfigurationLoads_ThenPortIsApplied(
        string value,
        int expected)
    {
        var values = ValidSettings();
        values["FORTUNA_METRICS_PORT"] = value;

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal(expected, options.MetricsPort);
    }

    [UnitTheory]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("metrics")]
    public void GivenInvalidMetricsPort_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_METRICS_PORT"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_METRICS_PORT", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenConfiguredConsentVersion_WhenConfigurationLoads_ThenValueIsNormalized()
    {
        var values = ValidSettings();
        values["FORTUNA_CONSENT_EXTERNAL_PROCESSING_VERSION"] = " 2026-09 ";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("2026-09", options.ConsentExternalDataProcessingVersion);
    }

    [UnitFact]
    public void GivenOversizedConsentVersion_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_CONSENT_EXTERNAL_PROCESSING_VERSION"] = new string('v', 51);

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_CONSENT_EXTERNAL_PROCESSING_VERSION", exception.Message,
            StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenConfiguredUploadPolicy_WhenConfigurationLoads_ThenValuesAreNormalized()
    {
        var values = ValidSettings();
        values["FORTUNA_UPLOAD_MAX_BYTES"] = "2048";
        values["FORTUNA_UPLOAD_ALLOWED_CONTENT_TYPES"] = " IMAGE/PNG, application/pdf,image/png ";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal(2048, options.UploadMaximumBytes);
        Assert.Equal(["image/png", "application/pdf"], options.UploadAllowedContentTypes);
    }

    [UnitTheory]
    [InlineData("FORTUNA_UPLOAD_MAX_BYTES", "0")]
    [InlineData("FORTUNA_UPLOAD_MAX_BYTES", "invalid")]
    [InlineData("FORTUNA_UPLOAD_ALLOWED_CONTENT_TYPES", "not-a-mime-type")]
    public void GivenInvalidUploadPolicy_WhenConfigurationLoads_ThenStartupIsRejected(
        string key,
        string value)
    {
        var values = ValidSettings();
        values[key] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
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

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
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

        var exception = Assert.Throws<FortunaConfigurationException>(
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

        var exception = Assert.Throws<FortunaConfigurationException>(
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

        var exception = Assert.Throws<FortunaConfigurationException>(
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
    public void GivenSqliteDatabaseType_WhenConfigurationLoads_ThenOfflineProviderIsAccepted()
    {
        var values = ValidSettings();
        values["FORTUNA_DATA_DATABASETYPE"] = "Sqlite";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("Sqlite", options.DataDatabaseType);
    }

    [UnitFact]
    public void GivenUnsupportedDatabaseType_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_DATA_DATABASETYPE"] = "MySql";

        var exception = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("PostgreSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SQLite", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInsecureHeimdallUrl_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_HEIMDALL_BASE_URL"] = "http://heimdall.example.test";

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenUnsupportedStorageProvider_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_STORAGE_PROVIDER"] = "database";

        var exception = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("Filesystem", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidQueueCapacity_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_JOB_QUEUE_CAPACITY"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("positive integer", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidHealthJobAge_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_HEALTH_JOB_MAX_PENDING_SECONDS"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("positive integer", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidMaximumPageSize_WhenConfigurationLoads_ThenStartupIsRejected(string value)
    {
        var values = ValidSettings();
        values["FORTUNA_PAGE_SIZE_MAX"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_PAGE_SIZE_MAX", exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidMaximumReportRange_WhenConfigurationLoads_ThenStartupIsRejected(
        string value)
    {
        var values = ValidSettings();
        values["FORTUNA_REPORT_MAX_RANGE_DAYS"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_REPORT_MAX_RANGE_DAYS", exception.Message,
            StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidReportKeyLifetime_WhenConfigurationLoads_ThenStartupIsRejected(
        string value)
    {
        var values = ValidSettings();
        values["FORTUNA_REPORT_KEY_TTL_MINUTES"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_REPORT_KEY_TTL_MINUTES", exception.Message,
            StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidProjectionHorizon_WhenConfigurationLoads_ThenStartupIsRejected(
        string value)
    {
        var values = ValidSettings();
        values["FORTUNA_PROJECTION_MAX_HORIZON_DAYS"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_PROJECTION_MAX_HORIZON_DAYS", exception.Message,
            StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("FORTUNA_EXPORT_SYNC_THRESHOLD_ROWS", "0")]
    [InlineData("FORTUNA_EXPORT_SYNC_THRESHOLD_ROWS", "not-a-number")]
    [InlineData("FORTUNA_EXPORT_RETENTION_HOURS", "0")]
    [InlineData("FORTUNA_EXPORT_RETENTION_HOURS", "not-a-number")]
    public void GivenInvalidExportBound_WhenConfigurationLoads_ThenStartupIsRejected(
        string key,
        string value)
    {
        var values = ValidSettings();
        values[key] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void GivenInvalidMaximumTagCount_WhenConfigurationLoads_ThenStartupIsRejected(
        string value)
    {
        var values = ValidSettings();
        values["FORTUNA_TRANSACTION_MAX_TAGS"] = value;

        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("FORTUNA_TRANSACTION_MAX_TAGS", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInvalidMigrationFlag_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_RUN_MIGRATIONS"] = "sometimes";

        var exception = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("true or false", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInvalidLocalAuthenticationFlag_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_LOCAL_AUTH_ENABLED"] = "sometimes";

        var exception = Assert.Throws<FortunaConfigurationException>(
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

        var exception = Assert.Throws<FortunaConfigurationException>(
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

        Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));
    }

    [UnitFact]
    public void GivenSeveralInvalidSettings_WhenConfigurationIsParsed_ThenEveryProblemIsReportedTogether()
    {
        var values = ValidSettings();
        values.Remove("FORTUNA_DATA_CONNECTIONSTRING");
        values["FORTUNA_JOB_QUEUE_CAPACITY"] = "zero";
        values["FORTUNA_AUTH_TOKEN_SECRET"] = "too-short";
        values["FORTUNA_LOCALE"] = "pt";

        var result = FortunaOptions.Parse(values.GetValueOrDefault);
        var exception = Assert.Throws<FortunaConfigurationException>(() =>
            FortunaOptions.From(values.GetValueOrDefault));

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Contains("FORTUNA_DATA_CONNECTIONSTRING", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("FORTUNA_JOB_QUEUE_CAPACITY", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("FORTUNA_AUTH_TOKEN_SECRET", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("FORTUNA_LOCALE", StringComparison.Ordinal));
        Assert.Equal(result.Errors, exception.Errors);
        Assert.All(result.Errors, error => Assert.Contains(error, exception.Message, StringComparison.Ordinal));
    }

    [UnitFact]
    public void GivenValidSettings_WhenConfigurationIsParsed_ThenNoErrorsAreReported()
    {
        var result = FortunaOptions.Parse(ValidSettings().GetValueOrDefault);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [UnitTheory]
    [InlineData("FORTUNA_AUTH_TOKEN_SECRET", "thirty-one-characters-long-key!")]
    [InlineData("FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS", "short-previous-secret")]
    public void GivenSigningSecretShorterThan32Bytes_WhenConfigurationIsParsed_ThenItIsRejected(
        string key,
        string secret)
    {
        var values = ValidSettings();
        values[key] = secret;

        var result = FortunaOptions.Parse(values.GetValueOrDefault);

        var error = Assert.Single(result.Errors);
        Assert.Contains(key, error, StringComparison.Ordinal);
        Assert.Contains("32", error, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenInvalidRateCurrencies_WhenConfigurationIsParsed_ThenTheRatesVariableIsNamed()
    {
        var values = ValidSettings();
        values["FORTUNA_RATES_SOURCE_BASE_URL"] = "https://rates.example.test/odata";
        values["FORTUNA_RATES_SYNC_CRON"] = "0 18 * * 1-5";
        values["FORTUNA_RATES_CURRENCIES"] = "BRL,DOLLAR";

        var result = FortunaOptions.Parse(values.GetValueOrDefault);

        Assert.Contains(result.Errors, error => error.StartsWith("FORTUNA_RATES_CURRENCIES", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error =>
            error.Contains("FORTUNA_DEFAULT_DISPLAY_CURRENCY", StringComparison.Ordinal));
    }

    [UnitFact]
    public void GivenInvalidRateCron_WhenConfigurationIsParsed_ThenTheCronReasonIsReported()
    {
        var values = ValidSettings();
        values["FORTUNA_RATES_SOURCE_BASE_URL"] = "https://rates.example.test/odata";
        values["FORTUNA_RATES_SYNC_CRON"] = "0 25 * * *";
        values["FORTUNA_RATES_CURRENCIES"] = "BRL,USD";

        var error = Assert.Single(FortunaOptions.Parse(values.GetValueOrDefault).Errors);

        Assert.Contains("FORTUNA_RATES_SYNC_CRON", error, StringComparison.Ordinal);
        Assert.Contains("hour", error, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenCommaDecimalCulture_WhenNumbersAreParsed_ThenInvariantFormatIsUsed()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var values = ValidSettings();
            values["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "1800.5";
            values["FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE"] = "0.05";

            var options = FortunaOptions.From(values.GetValueOrDefault);

            Assert.Equal(1800.5, options.AuthTokenExpirationInSeconds);
            Assert.Equal(0.05m, options.ReconciliationAmountTolerance);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [UnitTheory]
    [InlineData("9464", false)]
    [InlineData("8080;9464", true)]
    [InlineData("8080", true)]
    public void GivenApiPorts_WhenMetricsPortIsChecked_ThenAnApiServedOnlyOnTheMetricsPortIsRejected(
        string httpPorts,
        bool valid)
    {
        var values = ValidSettings();
        values["ASPNETCORE_HTTP_PORTS"] = httpPorts;

        var result = FortunaOptions.Parse(values.GetValueOrDefault);

        Assert.Equal(valid, result.IsValid);
        Assert.Equal(valid, !result.Errors.Any(error => error.Contains("FORTUNA_METRICS_PORT", StringComparison.Ordinal)));
    }

    [UnitFact]
    public void GivenForwardedProxySettings_WhenConfigurationIsParsed_ThenNetworksAndAddressesAreApplied()
    {
        var values = ValidSettings();
        values["FORTUNA_FORWARDED_KNOWN_NETWORKS"] = "10.0.0.0/8, 172.16.0.0/12";
        values["FORTUNA_FORWARDED_KNOWN_PROXIES"] = "192.168.1.10;fd00::1";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal(
            [System.Net.IPNetwork.Parse("10.0.0.0/8"), System.Net.IPNetwork.Parse("172.16.0.0/12")],
            options.ForwardedKnownNetworks);
        Assert.Equal(
            [System.Net.IPAddress.Parse("192.168.1.10"), System.Net.IPAddress.Parse("fd00::1")],
            options.ForwardedKnownProxies);
    }

    [UnitTheory]
    [InlineData("FORTUNA_FORWARDED_KNOWN_NETWORKS", "10.0.0.0")]
    [InlineData("FORTUNA_FORWARDED_KNOWN_NETWORKS", "not-a-network")]
    [InlineData("FORTUNA_FORWARDED_KNOWN_PROXIES", "10.0.0.0/8")]
    public void GivenInvalidForwardedProxySettings_WhenConfigurationIsParsed_ThenTheVariableIsNamed(
        string key,
        string value)
    {
        var values = ValidSettings();
        values[key] = value;

        var error = Assert.Single(FortunaOptions.Parse(values.GetValueOrDefault).Errors);

        Assert.Contains(key, error, StringComparison.Ordinal);
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
        ["FORTUNA_EXPORT_SYNC_THRESHOLD_ROWS"] = "1000",
        ["FORTUNA_EXPORT_RETENTION_HOURS"] = "24",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
        ,
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test"
        ,
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076"
    };
}
