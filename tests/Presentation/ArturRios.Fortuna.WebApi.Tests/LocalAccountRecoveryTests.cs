using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class LocalAccountRecoveryTests : IAsyncLifetime
{
    private const string OriginalSecret = "correct-horse-battery-staple";
    private const string NewSecret = "new-correct-horse-battery-staple";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenUnusedRecoveryCode_WhenRecovering_ThenCodeIsSpentAndNewSecretAuthenticates()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        var recoveryCode = created.RecoveryCodes.First();

        var response = await RecoverAsync(client, recoveryCode, NewSecret);
        var recovered = await response.Content.ReadFromJsonAsync<RecoveryEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(recovered?.Data);
        Assert.NotEmpty(recovered.Data.Token);
        Assert.Equal(9, recovered.Data.RemainingRecoveryCodes);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", recovered.Data.Token);
        var profile = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");
        Assert.Equal(created.UserId, profile!.Data!.Id);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthenticateAsync(client, OriginalSecret)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthenticateAsync(client, NewSecret)).StatusCode);

        await using var context = CreateContext();
        var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
        Assert.True(Hash.TextMatches(NewSecret, account.SecretHash, account.Salt));
        Assert.False(Hash.TextMatches(OriginalSecret, account.SecretHash, account.Salt));
        Assert.NotNull(account.RecoveryCodes.Single(code =>
            code.CodeHash.SequenceEqual(HashRecoveryCode(recoveryCode))).UsedAt);
        Assert.Equal(9, account.RecoveryCodes.Count(code => code.UsedAt is null));
    }

    [FunctionalFact]
    public async Task GivenInvalidRecoveryCode_WhenRecovering_ThenNothingChanges()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        _ = await CreateAccountAsync(client);

        var response = await RecoverAsync(client, "WRNG-CODE", NewSecret);
        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(LocalAccountRecoveryMessages.InvalidRecoveryCode, error!.Errors);
        await using var context = CreateContext();
        var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
        Assert.True(Hash.TextMatches(OriginalSecret, account.SecretHash, account.Salt));
        Assert.All(account.RecoveryCodes, code => Assert.Null(code.UsedAt));
    }

    [FunctionalFact]
    public async Task GivenAlreadyUsedRecoveryCode_WhenRecoveringAgain_ThenUnauthorizedLeavesFirstSecret()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        var recoveryCode = created.RecoveryCodes.First();
        _ = await RecoverAsync(client, recoveryCode, NewSecret);

        var response = await RecoverAsync(client, recoveryCode, "second-valid-secret");
        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(LocalAccountRecoveryMessages.InvalidRecoveryCode, error!.Errors);
        await using var context = CreateContext();
        var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
        Assert.True(Hash.TextMatches(NewSecret, account.SecretHash, account.Salt));
        Assert.Equal(1, account.RecoveryCodes.Count(code => code.UsedAt is not null));
    }

    [FunctionalFact]
    public async Task GivenEveryRecoveryCodeIsUsed_WhenRecovering_ThenUnrecoverableReasonIsReturned()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        await using (var setup = CreateContext())
        {
            var account = await setup.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
            foreach (var code in account.RecoveryCodes)
            {
                code.MarkUsed(DateTimeOffset.UtcNow);
            }

            await setup.SaveChangesAsync();
        }

        var response = await RecoverAsync(client, created.RecoveryCodes.First(), NewSecret);
        var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(LocalAccountRecoveryMessages.RecoveryCodesExhausted, error!.Errors);
        await using var context = CreateContext();
        var stored = await context.LocalAccounts.SingleAsync();
        Assert.True(Hash.TextMatches(OriginalSecret, stored.SecretHash, stored.Salt));
    }

    [FunctionalFact]
    public async Task GivenInvalidNewSecret_WhenRecovering_ThenCodeCanBeRetried()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var created = await CreateAccountAsync(client);
        var recoveryCode = created.RecoveryCodes.First();

        var invalid = await RecoverAsync(client, recoveryCode, "short");
        var invalidBody = await invalid.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(LocalAccountRecoveryMessages.NewSecretTooShort, invalidBody!.Errors);
        await using (var context = CreateContext())
        {
            var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
            Assert.True(Hash.TextMatches(OriginalSecret, account.SecretHash, account.Salt));
            Assert.All(account.RecoveryCodes, code => Assert.Null(code.UsedAt));
        }

        Assert.Equal(HttpStatusCode.OK, (await RecoverAsync(client, recoveryCode, NewSecret)).StatusCode);
    }

    [FunctionalFact]
    public async Task GivenTwoConcurrentRequests_WhenRecoveringWithOneCode_ThenOnlyOneSucceeds()
    {
        await using var factory = CreateFactory(enabled: true);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var created = await CreateAccountAsync(firstClient);
        var recoveryCode = created.RecoveryCodes.First();

        var responses = await Task.WhenAll(
            RecoverAsync(firstClient, recoveryCode, "first-concurrent-secret"),
            RecoverAsync(secondClient, recoveryCode, "second-concurrent-secret"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        await using var context = CreateContext();
        var account = await context.LocalAccounts.Include(x => x.RecoveryCodes).SingleAsync();
        Assert.Equal(1, account.RecoveryCodes.Count(code => code.UsedAt is not null));
        Assert.True(
            Hash.TextMatches("first-concurrent-secret", account.SecretHash, account.Salt) ||
            Hash.TextMatches("second-concurrent-secret", account.SecretHash, account.Salt));
    }

    [FunctionalFact]
    public async Task GivenLocalAuthenticationDisabled_WhenRecovering_ThenEndpointIsHidden()
    {
        await using var factory = CreateFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await RecoverAsync(client, "ABCD-1234", NewSecret);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            Secret = OriginalSecret,
            StorageMode = LocalAccountStorageMode.InMemory
        });
        var envelope = await response.Content.ReadFromJsonAsync<CreationEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return envelope!.Data!;
    }

    private static Task<HttpResponseMessage> RecoverAsync(
        HttpClient client,
        string recoveryCode,
        string newSecret) => client.PostAsJsonAsync("/api/local-accounts/recover", new
        {
            name = "Local User",
            recoveryCode,
            newSecret
        });

    private static Task<HttpResponseMessage> AuthenticateAsync(HttpClient client, string secret) =>
        client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            name = "Local User",
            secret
        });

    private WebApplicationFactory<Program> CreateFactory(bool enabled)
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
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = enabled.ToString(),
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record CreationEnvelope(CreationData? Data);
    private sealed record CreationData(Guid UserId, IReadOnlyCollection<string> RecoveryCodes);
    private sealed record RecoveryEnvelope(RecoveryData? Data);
    private sealed record RecoveryData(string Token, DateTimeOffset ExpiresAt, int RemainingRecoveryCodes);
    private sealed record ProfileEnvelope(ProfileData? Data);
    private sealed record ProfileData(Guid Id);
    private sealed record ErrorEnvelope(IReadOnlyCollection<string> Errors);
}
