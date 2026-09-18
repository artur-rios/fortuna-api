using System.Net;
using System.Net.Http.Headers;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class PrometheusMetricsTests : IAsyncLifetime
{
    private const int MetricsPort = 9464;
    private const int PublicPort = 8080;
    private const string LocalPortHeader = "X-Test-Local-Port";
    private const string Secret = "fortuna-metrics-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-metrics-tests";
    private const string Audience = "fortuna-metrics-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string storageRoot = Path.Combine(
        Path.GetTempPath(), "fortuna-metrics-tests", Guid.NewGuid().ToString("N"));

    [FunctionalFact]
    public async Task GivenPublicListener_WhenMetricsRequested_ThenEndpointIsNotServed()
    {
        await using var factory = CreateFactory(metricsPort: null);
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("# TYPE", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenForgedHostHeaderOnPublicListener_WhenMetricsRequested_ThenEndpointIsNotServed()
    {
        await using var factory = CreateFactory(metricsPort: null);
        using var client = factory.CreateClient();
        Authorize(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Host = $"localhost:{MetricsPort}";
        request.Headers.Add(LocalPortHeader, PublicPort.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenPrivateListener_WhenAnonymousScrapeRequested_ThenPrometheusTextIsServed()
    {
        await using var factory = CreateFactory(metricsPort: null);
        using var client = factory.CreateClient();
        Authorize(client);
        _ = await client.GetAsync("/healthcheck");
        using var scraper = factory.CreateClient();
        scraper.DefaultRequestHeaders.Add(LocalPortHeader, MetricsPort.ToString());

        var response = await scraper.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("# TYPE http_server_request_duration_seconds histogram", body,
            StringComparison.Ordinal);
        Assert.Contains("# TYPE dotnet_gc_heap_total_allocated_bytes_total counter", body,
            StringComparison.Ordinal);
        Assert.Contains("service_name=\"fortuna-api\"", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenExporterDisabled_WhenPrivateListenerRequestsMetrics_ThenEndpointIsNotServed()
    {
        await using var factory = CreateFactory(metricsPort: "0");
        using var client = factory.CreateClient();
        Authorize(client);
        client.DefaultRequestHeaders.Add(LocalPortHeader, MetricsPort.ToString());

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(factory.Services.GetService<MeterProvider>());
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
        Environment.SetEnvironmentVariable("FORTUNA_METRICS_PORT", null);
        await database.DisposeAsync();
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string? metricsPort)
    {
        foreach (var setting in ValidSettings(metricsPort))
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
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
                services.AddSingleton<IStartupFilter, SimulatedListenerStartupFilter>();
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

    private static void Authorize(HttpClient client)
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.SystemAdmin, Guid.NewGuid(), [])
        {
            DisplayName = "Metrics Caller"
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

    private Dictionary<string, string?> ValidSettings(string? metricsPort) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = storageRoot,
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(
            Path.GetTempPath(), "fortuna-api-metrics-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_METRICS_PORT"] = metricsPort,
        ["FORTUNA_PLUGGY_CLIENT_ID"] = null,
        ["FORTUNA_PLUGGY_CLIENT_SECRET"] = null,
        ["FORTUNA_PLUGGY_BASE_URL"] = null,
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = null,
        ["FORTUNA_RATES_SYNC_CRON"] = null,
        ["FORTUNA_RATES_CURRENCIES"] = null
    };

    /// <summary>
    /// TestServer reports local port 0 for every request, so this stands in for Kestrel's second
    /// listener: it runs ahead of the application's pipeline and adopts the port a test names.
    /// </summary>
    private sealed class SimulatedListenerStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (int.TryParse(context.Request.Headers[LocalPortHeader], out var port))
                {
                    context.Connection.LocalPort = port;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
