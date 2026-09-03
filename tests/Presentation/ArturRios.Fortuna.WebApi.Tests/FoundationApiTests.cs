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
        Assert.False(options.RunMigrations);
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

    [UnitFact]
    public void GivenInvalidMigrationFlag_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        var values = ValidSettings();
        values["FORTUNA_RUN_MIGRATIONS"] = "sometimes";

        var exception = Assert.Throws<InvalidOperationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains("true or false", exception.Message, StringComparison.Ordinal);
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/healthcheck", await response.Content.ReadAsStringAsync(CancellationToken.None));
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
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32"
    };
}
