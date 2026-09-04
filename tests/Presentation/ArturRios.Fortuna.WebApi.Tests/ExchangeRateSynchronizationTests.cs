using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;
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

public sealed class ExchangeRateSynchronizationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenConfiguredSource_WhenSynchronizationIsRequested_ThenPendingJobIsPersistedAndQueued()
    {
        var queue = new RecordingQueue();
        await using var factory = CreateFactory(configured: true, queue);
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/sync",
            new { RequestedDate = "2026-09-01" });
        var envelope = await response.Content.ReadFromJsonAsync<JobEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.Equal(new DateOnly(2026, 9, 1), envelope.Data.RequestedDate);
        Assert.Equal(envelope.Data.JobId, queue.JobId);
        await using var context = CreateContext();
        var job = await context.BackgroundJobs.SingleAsync(job => job.Id == envelope.Data.JobId);
        Assert.Equal(ExchangeRateSyncJob.Type, job.Type);
        Assert.Equal(BackgroundJobState.Pending, job.State);
        Assert.NotNull(job.CorrelationId);
        Assert.Equal(
            new DateOnly(2026, 9, 1),
            JsonSerializer.Deserialize<ExchangeRateSyncJobPayload>(job.Payload)!.RequestedDate);
    }

    [FunctionalFact]
    public async Task GivenSourceNotConfigured_WhenSynchronizationIsRequested_ThenBadRequestCreatesNoJob()
    {
        await using var beforeContext = CreateContext();
        var before = await beforeContext.BackgroundJobs.CountAsync();
        await using var factory = CreateFactory(configured: false, new RecordingQueue());
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.PostAsJsonAsync("/api/exchange-rates/sync", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var afterContext = CreateContext();
        Assert.Equal(before, await afterContext.BackgroundJobs.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenSynchronizationIsRequested_ThenUnauthorizedCreatesNoJob()
    {
        await using var beforeContext = CreateContext();
        var before = await beforeContext.BackgroundJobs.CountAsync();
        await using var factory = CreateFactory(configured: true, new RecordingQueue());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/exchange-rates/sync", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var afterContext = CreateContext();
        Assert.Equal(before, await afterContext.BackgroundJobs.CountAsync());
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(bool configured, RecordingQueue queue)
    {
        foreach (var setting in ValidSettings(configured))
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
                services.RemoveAll<IBackgroundJobQueue>();
                services.AddDbContext<AppDbContext>(databaseOptions =>
                    databaseOptions.UseNpgsql(database.GetConnectionString()));
                services.AddSingleton<IBackgroundJobQueue>(queue);
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
        var identity = new FortunaIdentity(
            Guid.NewGuid(),
            (int)HeimdallRoles.User,
            Guid.NewGuid(),
            [])
        {
            DisplayName = "Rate User"
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

    private static Dictionary<string, string?> ValidSettings(bool configured) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = configured
            ? "https://rates.example.test/odata/"
            : null,
        ["FORTUNA_RATES_SYNC_CRON"] = configured ? "0 18 * * 1-5" : null,
        ["FORTUNA_RATES_CURRENCIES"] = configured ? "BRL,USD,EUR" : null
    };

    private sealed class RecordingQueue : IBackgroundJobQueue
    {
        public int Depth => JobId.HasValue ? 1 : 0;
        public Guid? JobId { get; private set; }

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record JobEnvelope(JobData? Data);
    private sealed record JobData(Guid JobId, DateOnly RequestedDate);
}
