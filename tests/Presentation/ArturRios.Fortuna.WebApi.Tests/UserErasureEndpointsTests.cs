using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;
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

public sealed class UserErasureEndpointsTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenConfirmedOwner_WhenErasureRequested_ThenIrreversibleCountsAreReturned()
    {
        var store = new RecordingErasureStore(Result());
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        await CreateConnectedTargetAsync(subject);
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/me/erasure",
            new { Confirmation = "ERASE" });
        var body = await response.Content.ReadFromJsonAsync<ErasureEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.Data!.Irreversible);
        Assert.Equal(4, body.Data.Erased["financialRecords"]);
        Assert.Equal(1, body.Data.RevokedConnections);
        Assert.NotNull(store.UserId);
    }

    [FunctionalFact]
    public async Task GivenMissingOrMismatchedConfirmation_WhenErasureRequested_ThenBadRequestStoresNothing()
    {
        var store = new RecordingErasureStore(Result());
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        await CreateConnectedTargetAsync(subject);
        Authorize(client, subject, HeimdallRoles.User);

        var missing = await client.PostAsJsonAsync("/api/me/erasure", new { });
        var mismatched = await client.PostAsJsonAsync(
            "/api/me/erasure", new { Confirmation = "erase" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Null(store.UserId);
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenTargetErased_ThenTargetIdIsUsedWithoutRecordContent()
    {
        var targetId = await CreateTargetAsync();
        var store = new RecordingErasureStore(Result());
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{targetId}")
        {
            Content = JsonContent.Create(new { Confirmation = "ERASE" })
        };

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<ErasureEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(targetId, store.UserId);
        Assert.NotNull(body?.Data?.Erased);
    }

    [FunctionalFact]
    public async Task GivenNonAdministratorOrUnknownTarget_WhenTargetErased_ThenNotFoundStoresNothing()
    {
        var targetId = await CreateTargetAsync();
        var nonAdminStore = new RecordingErasureStore(Result());
        await using (var factory = CreateFactory(nonAdminStore))
        using (var client = factory.CreateClient())
        {
            Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
            using var request = Delete(targetId);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(nonAdminStore.UserId);
        }

        var missingStore = new RecordingErasureStore(Result());
        await using (var factory = CreateFactory(missingStore))
        using (var client = factory.CreateClient())
        {
            Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
            using var request = Delete(Guid.NewGuid());
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(missingStore.UserId);
        }
    }

    [FunctionalFact]
    public async Task GivenAnonymousCaller_WhenErasureRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory(new RecordingErasureStore(Result()));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/me/erasure", new { Confirmation = "ERASE" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenErasedOrNeverProvisionedSubject_WhenErasureRequested_ThenProfileIsNotRecreated()
    {
        var subject = Guid.NewGuid();
        var store = new RecordingErasureStore(Result());
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/me/erasure",
            new { Confirmation = "ERASE" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(store.UserId);
        await using var context = CreateContext();
        Assert.False(await context.UserProfiles.AnyAsync(
            item => item.ExternalSubject == subject.ToString("D")));
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> CreateTargetAsync()
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var target = new UserProfile(Guid.NewGuid(), "Erasure Target", currency, DateTimeOffset.UtcNow);
        context.UserProfiles.Add(target);
        await context.SaveChangesAsync();
        return target.PublicId;
    }

    private async Task CreateConnectedTargetAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        context.UserProfiles.Add(new UserProfile(
            subject,
            "Erasure Actor",
            currency,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(RecordingErasureStore store)
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
                services.RemoveAll<IUserErasureStore>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
                services.AddSingleton<IUserErasureStore>(store);
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

    private static HttpRequestMessage Delete(Guid id) => new(HttpMethod.Delete, $"/api/users/{id}")
    {
        Content = JsonContent.Create(new { Confirmation = "ERASE" })
    };

    private static UserErasureResult Result() => new(
        Guid.NewGuid(),
        new Dictionary<string, int> { ["financialRecords"] = 4, ["profiles"] = 1 },
        1);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Erasure Actor"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-erasure-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-erasure-api-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed class RecordingErasureStore(UserErasureResult? result) : IUserErasureStore
    {
        public Guid? UserId { get; private set; }

        public Task<UserErasureResult?> EraseAsync(
            Guid userId,
            DateTimeOffset erasedAt,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            return Task.FromResult(result);
        }
    }

    private sealed record ErasureEnvelope(ErasureData? Data);
    private sealed record ErasureData(
        IReadOnlyDictionary<string, int> Erased,
        int RevokedConnections,
        bool Irreversible);
}
