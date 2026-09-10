using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class HealthCheckTests : IAsyncLifetime
{
    private const string Secret = "fortuna-health-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-health-tests";
    private const string Audience = "fortuna-health-tests";
    private static readonly DateTimeOffset Now = new(
        2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string storageRoot = Path.Combine(
        Path.GetTempPath(), "fortuna-health-tests", Guid.NewGuid().ToString("N"));

    [FunctionalFact]
    public async Task GivenAnonymousCaller_WhenLivenessRequested_ThenDependenciesAreNotTouched()
    {
        await using var factory = CreateFactory([
            new ThrowingOperationalHealthCheck()
        ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthcheck");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var publishedContract = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "docs", "openapi", "fortuna.json")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal("Fortuna API", document.RootElement.GetProperty("service").GetString());
        Assert.Equal(
            publishedContract.RootElement.GetProperty("info").GetProperty("version").GetString(),
            document.RootElement.GetProperty("contractVersion").GetString());
        Assert.False(document.RootElement.TryGetProperty("status", out _));
        Assert.False(document.RootElement.TryGetProperty("buildVersion", out _));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "docs", "openapi", "fortuna.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

    [FunctionalFact]
    public async Task GivenHealthyDependencies_WhenDetailedHealthRequested_ThenSafeReportIsAuthorized()
    {
        await using var factory = CreateFactory();
        using var administrator = factory.CreateClient();
        Authorize(administrator, HeimdallRoles.SystemAdmin);
        using var user = factory.CreateClient();
        Authorize(user, HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var invalid = factory.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "not-a-token");

        var response = await administrator.GetAsync("/healthcheck/detailed");
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var services = document.RootElement.GetProperty("services");
        var forbidden = await user.GetAsync("/healthcheck/detailed");
        var unauthorized = await anonymous.GetAsync("/healthcheck/detailed");
        var invalidToken = await invalid.GetAsync("/healthcheck/detailed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, services.GetArrayLength());
        Assert.Equal("Healthy", Service(services, "Database").GetProperty("status").GetString());
        Assert.Equal("Healthy", Service(services, "AttachmentStorage")
            .GetProperty("status").GetString());
        var runner = Service(services, "JobRunner");
        Assert.Equal("Healthy", runner.GetProperty("status").GetString());
        Assert.Equal(0, runner.GetProperty("queueDepth").GetInt32());
        Assert.Equal(0, runner.GetProperty("oldestPendingSeconds").GetInt64());
        Assert.Equal("NotConfigured", Service(services, "Aggregator")
            .GetProperty("status").GetString());
        Assert.Equal("NotConfigured", Service(services, "ExchangeRateSource")
            .GetProperty("status").GetString());
        Assert.DoesNotContain("Password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenRequiredOrOptionalDependencyDown_WhenDetailedHealthRequested_ThenAggregateDiffers()
    {
        await using var requiredFactory = CreateFactory(Checks(
            databaseStatus: OperationalHealthStatus.Unhealthy,
            aggregatorStatus: OperationalHealthStatus.NotConfigured));
        using var requiredClient = requiredFactory.CreateClient();
        Authorize(requiredClient, HeimdallRoles.SystemAdmin);
        await using var optionalFactory = CreateFactory(Checks(
            databaseStatus: OperationalHealthStatus.Healthy,
            aggregatorStatus: OperationalHealthStatus.Degraded));
        using var optionalClient = optionalFactory.CreateClient();
        Authorize(optionalClient, HeimdallRoles.SystemAdmin);

        var required = await requiredClient.GetAsync("/healthcheck/detailed");
        var requiredText = await required.Content.ReadAsStringAsync();
        var optional = await optionalClient.GetAsync("/healthcheck/detailed");
        var optionalText = await optional.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, required.StatusCode);
        Assert.Contains("\"status\":\"Unhealthy\"", requiredText);
        Assert.Contains("\"name\":\"Database\",\"status\":\"Unhealthy\"",
            requiredText);
        Assert.Equal(HttpStatusCode.OK, optional.StatusCode);
        Assert.Contains("\"status\":\"Degraded\"", optionalText);
        Assert.Contains("\"name\":\"Aggregator\",\"status\":\"Degraded\"",
            optionalText);
    }

    [FunctionalFact]
    public async Task GivenStalePendingJob_WhenDetailedHealthRequested_ThenRunnerIsUnhealthy()
    {
        Guid jobId;
        await using (var context = CreateContext())
        {
            var job = BackgroundJob.Create(
                "health-test",
                "{}",
                $"health-test:{Guid.NewGuid():N}",
                null,
                Now.AddSeconds(-301));
            context.BackgroundJobs.Add(job);
            await context.SaveChangesAsync();
            jobId = job.Id;
        }

        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient();
            Authorize(client, HeimdallRoles.SystemAdmin);

            var response = await client.GetAsync("/healthcheck/detailed");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var runner = Service(document.RootElement.GetProperty("services"), "JobRunner");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("Unhealthy", runner.GetProperty("status").GetString());
            Assert.Equal(301, runner.GetProperty("oldestPendingSeconds").GetInt64());
        }
        finally
        {
            await using var context = CreateContext();
            await context.BackgroundJobs.Where(job => job.Id == jobId).ExecuteDeleteAsync();
        }
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        IReadOnlyCollection<IOperationalHealthCheck>? checks = null)
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<OperationalHealthOptions>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                services.AddSingleton(new OperationalHealthOptions(TimeSpan.FromSeconds(300)));
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
                if (checks is not null)
                {
                    services.RemoveAll<IOperationalHealthCheck>();
                    foreach (var check in checks)
                    {
                        services.AddSingleton(check);
                    }
                }
            });
        });
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        return new AppDbContext(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static JsonElement Service(JsonElement services, string name) =>
        services.EnumerateArray().Single(service =>
            service.GetProperty("name").GetString() == name);

    private static IReadOnlyCollection<IOperationalHealthCheck> Checks(
        OperationalHealthStatus databaseStatus,
        OperationalHealthStatus aggregatorStatus) =>
    [
        new StubOperationalHealthCheck("Database", databaseStatus, required: true),
        new StubOperationalHealthCheck(
            "AttachmentStorage", OperationalHealthStatus.Healthy, required: true),
        new StubOperationalHealthCheck(
            "JobRunner", OperationalHealthStatus.Healthy, required: true, 0, 0),
        new StubOperationalHealthCheck("Aggregator", aggregatorStatus, required: false),
        new StubOperationalHealthCheck(
            "ExchangeRateSource", OperationalHealthStatus.NotConfigured, required: false)
    ];

    private static void Authorize(HttpClient client, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Health Caller"
        };
        var configuration = new JwtConfiguration(
            3600,
            Issuer,
            Audience,
            Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtHandler().CreateToken(configuration));
    }

    private Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = storageRoot,
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(
            Path.GetTempPath(), "fortuna-api-health-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_HEALTH_JOB_MAX_PENDING_SECONDS"] = "300",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_PLUGGY_CLIENT_ID"] = null,
        ["FORTUNA_PLUGGY_CLIENT_SECRET"] = null,
        ["FORTUNA_PLUGGY_BASE_URL"] = null,
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = null,
        ["FORTUNA_RATES_SYNC_CRON"] = null,
        ["FORTUNA_RATES_CURRENCIES"] = null
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubOperationalHealthCheck(
        string name,
        OperationalHealthStatus status,
        bool required,
        int? queueDepth = null,
        long? oldestPendingSeconds = null) : IOperationalHealthCheck
    {
        public string Name => name;
        public bool IsRequired => required;

        public Task<OperationalHealthCheckResult> CheckAsync(
            CancellationToken cancellationToken) => Task.FromResult(
            new OperationalHealthCheckResult(
                Name,
                status,
                IsRequired,
                queueDepth,
                oldestPendingSeconds));
    }

    private sealed class ThrowingOperationalHealthCheck : IOperationalHealthCheck
    {
        public string Name => "Throwing";
        public bool IsRequired => true;

        public Task<OperationalHealthCheckResult> CheckAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException(
            "The liveness endpoint must not evaluate dependencies.");
    }
}
