using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
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

public sealed class LocalRecoveryCodeRegenerationTests : IAsyncLifetime
{
    private const string Secret = "correct-horse-battery-staple";
    private const string SigningSecret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLocalActorAndMatchingSecret_WhenRegenerating_ThenOldCodesAreReplaced()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        var oldHashes = created.RecoveryCodes.Select(HashRecoveryCode).Select(Convert.ToHexString).ToHashSet();
        await AuthorizeAsLocalAsync(client);

        var response = await RegenerateAsync(client, Secret);
        var regenerated = await response.Content.ReadFromJsonAsync<RegenerationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(regenerated?.Data);
        Assert.Equal(10, regenerated.Data.RecoveryCodes.Count);
        Assert.Equal(10, regenerated.Data.RecoveryCodes.Distinct().Count());
        Assert.All(regenerated.Data.RecoveryCodes, code =>
            Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", code));
        Assert.Equal(LocalAccountMessages.RecoveryWarning, regenerated.Data.RecoveryWarning);
        await using (var context = CreateContext())
        {
            var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
            Assert.Equal(10, account.RecoveryCodes.Count);
            Assert.All(account.RecoveryCodes, code => Assert.Null(code.UsedAt));
            Assert.DoesNotContain(account.RecoveryCodes, code =>
                oldHashes.Contains(Convert.ToHexString(code.CodeHash)));
            Assert.Equal(
                regenerated.Data.RecoveryCodes.Select(HashRecoveryCode).Select(Convert.ToHexString).Order(),
                account.RecoveryCodes.Select(code => Convert.ToHexString(code.CodeHash)).Order());
        }

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await RecoverAsync(client, created.RecoveryCodes.First(), "old-code-secret")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await RecoverAsync(client, regenerated.Data.RecoveryCodes.First(), "new-code-secret")).StatusCode);
    }

    [FunctionalFact]
    public async Task GivenWrongSecret_WhenRegenerating_ThenExistingCodesRemainValid()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        await AuthorizeAsLocalAsync(client);

        var response = await RegenerateAsync(client, "wrong-secret");
        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(LocalRecoveryCodeRegenerationMessages.InvalidSecret, error!.Errors);
        await AssertStoredHashesEqualAsync(created.RecoveryCodes);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.OK,
            (await RecoverAsync(client, created.RecoveryCodes.First(), "recovered-secret")).StatusCode);
    }

    [FunctionalFact]
    public async Task GivenHeimdallActor_WhenRegenerating_ThenLocalOnlyEndpointIsHidden()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", HeimdallToken());

        var response = await RegenerateAsync(client, Secret);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredHashesEqualAsync(created.RecoveryCodes);
    }

    [FunctionalFact]
    public async Task GivenGenerationFails_WhenRegenerating_ThenExistingCodesRemainValid()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        var token = await LocalTokenAsync(client);
        await using var failingFactory = CreateFactory(
            enabled: true,
            services =>
            {
                services.RemoveAll<ILocalRecoveryCodeGenerator>();
                services.AddSingleton<ILocalRecoveryCodeGenerator, ThrowingRecoveryCodeGenerator>();
            });
        using var failingClient = failingFactory.CreateClient();
        failingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await RegenerateAsync(failingClient, Secret);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStoredHashesEqualAsync(created.RecoveryCodes);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.OK,
            (await RecoverAsync(client, created.RecoveryCodes.First(), "recovered-secret")).StatusCode);
    }

    [FunctionalFact]
    public async Task GivenLocalAuthenticationDisabled_WhenRegenerating_ThenEndpointIsHidden()
    {
        await using var enabledFactory = CreateFactory(enabled: true);
        using var enabledClient = enabledFactory.CreateClient();
        var created = await CreateAccountAsync(enabledClient);
        var token = await LocalTokenAsync(enabledClient);
        await using var disabledFactory = CreateFactory(enabled: false);
        using var disabledClient = disabledFactory.CreateClient();
        disabledClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await RegenerateAsync(disabledClient, Secret);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredHashesEqualAsync(created.RecoveryCodes);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenRegenerating_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();

        var response = await RegenerateAsync(client, Secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(0, await context.LocalAccounts.CountAsync());
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<CreationData> CreateAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/local-accounts", new CreateLocalAccountCommand
        {
            DisplayName = "Local User",
            Secret = Secret,
            StorageMode = LocalAccountStorageMode.InMemory
        });
        var envelope = await response.Content.ReadFromJsonAsync<CreationEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return envelope!.Data!;
    }

    private static Task<HttpResponseMessage> RegenerateAsync(HttpClient client, string secret) =>
        client.PostAsJsonAsync("/api/local-accounts/recovery-codes/regenerate", new { secret });

    private static Task<HttpResponseMessage> RecoverAsync(
        HttpClient client,
        string recoveryCode,
        string newSecret) => client.PostAsJsonAsync("/api/local-accounts/recover", new
        {
            name = "Local User",
            recoveryCode,
            newSecret
        });

    private async Task AuthorizeAsLocalAsync(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LocalTokenAsync(client));

    private static async Task<string> LocalTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            name = "Local User",
            secret = Secret
        });
        var envelope = await response.Content.ReadFromJsonAsync<AuthenticationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return envelope!.Data!.Token;
    }

    private static string HeimdallToken()
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Heimdall User"
        };
        return new JwtHandler().CreateToken(new JwtConfiguration(
            3600,
            Issuer,
            Audience,
            SigningSecret,
            new FortunaIdentityMapper().ToClaims(identity)));
    }

    private async Task AssertStoredHashesEqualAsync(IEnumerable<string> recoveryCodes)
    {
        var expected = recoveryCodes.Select(HashRecoveryCode).Select(Convert.ToHexString).Order();
        await using var context = CreateContext();
        var stored = await context.RecoveryCodes
            .OrderBy(code => code.Id)
            .Select(code => code.CodeHash)
            .ToArrayAsync();
        var actual = stored.Select(Convert.ToHexString).Order();

        Assert.Equal(expected, actual);
    }

    private WebApplicationFactory<Program> CreateFactory(
        bool enabled,
        Action<IServiceCollection>? configure = null)
    {
        foreach (var setting in ValidSettings(enabled))
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
                services.AddDbContext<AppDbContext>(databaseOptions =>
                    databaseOptions.UseNpgsql(database.GetConnectionString()));
                configure?.Invoke(services);
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

    private static byte[] HashRecoveryCode(string recoveryCode) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(recoveryCode));

    private static Dictionary<string, string?> ValidSettings(bool enabled) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = SigningSecret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = enabled.ToString(),
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed class ThrowingRecoveryCodeGenerator : ILocalRecoveryCodeGenerator
    {
        public IReadOnlyCollection<GeneratedRecoveryCode> Generate(int count) =>
            throw new InvalidOperationException("Simulated partial generation failure.");
    }

    private sealed record CreationEnvelope(CreationData? Data);
    private sealed record CreationData(Guid UserId, IReadOnlyCollection<string> RecoveryCodes);
    private sealed record AuthenticationEnvelope(AuthenticationData? Data);
    private sealed record AuthenticationData(string Token);
    private sealed record RegenerationEnvelope(RegenerationData? Data);
    private sealed record RegenerationData(
        IReadOnlyCollection<string> RecoveryCodes,
        string RecoveryWarning);
    private sealed record ErrorEnvelope(IReadOnlyCollection<string> Errors);
}
