using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Security;
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

public sealed class LocalAccountCreationTests : IAsyncLifetime
{
    private const string Secret = "correct-horse-battery-staple";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenDesktopModeAndNoAccount_WhenCreatingAccount_ThenSecretsAreHashedAndCodesReturnedOnce()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/local-accounts", ValidCommand());
        var envelope = await response.Content.ReadFromJsonAsync<CreationEnvelope>();
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.Equal("Local User", envelope.Data.DisplayName);
        Assert.Equal(LocalAccountStorageMode.InMemory, envelope.Data.StorageMode);
        Assert.Equal(10, envelope.Data.RecoveryCodes.Count);
        Assert.Equal(10, envelope.Data.RecoveryCodes.Distinct().Count());
        Assert.All(envelope.Data.RecoveryCodes, code =>
            Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", code));
        Assert.Equal(LocalAccountMessages.RecoveryWarning, envelope.Data.RecoveryWarning);
        Assert.DoesNotContain(Secret, responseBody, StringComparison.Ordinal);

        await using var context = CreateContext();
        var account = await context.LocalAccounts
            .Include(x => x.User)
            .Include(x => x.RecoveryCodes)
            .SingleAsync();
        Assert.Null(account.User.ExternalSubject);
        Assert.True(Hash.TextMatches(Secret, account.SecretHash, account.Salt));
        Assert.Equal(10, account.RecoveryCodes.Count);
        Assert.Equal(
            envelope.Data.RecoveryCodes.Select(HashRecoveryCode).OrderBy(hash => Convert.ToHexString(hash)),
            account.RecoveryCodes.Select(x => x.CodeHash).OrderBy(hash => Convert.ToHexString(hash)),
            ByteArrayComparer.Instance);
        Assert.All(account.RecoveryCodes, code =>
            Assert.DoesNotContain(envelope.Data.RecoveryCodes, raw =>
                code.CodeHash.SequenceEqual(Encoding.UTF8.GetBytes(raw))));
    }

    [FunctionalFact]
    public async Task GivenLocalAccountAlreadyExists_WhenCreatingAnother_ThenConflictReturnsNoCodes()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        _ = await client.PostAsJsonAsync("/api/local-accounts", ValidCommand());

        var second = await client.PostAsJsonAsync("/api/local-accounts", new CreateLocalAccountCommand
        {
            DisplayName = "Replacement User",
            Secret = "another-valid-secret",
            StorageMode = LocalAccountStorageMode.InMemory
        });
        var body = await second.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(LocalAccountMessages.AlreadyExists, body, StringComparison.Ordinal);
        Assert.DoesNotContain("recoveryCodes", body, StringComparison.OrdinalIgnoreCase);
        await using var context = CreateContext();
        Assert.Equal(1, await context.LocalAccounts.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenLocalAuthenticationDisabled_WhenCreatingAccount_ThenEndpointIsHidden()
    {
        await using var factory = CreateFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/local-accounts", ValidCommand());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(0, await context.LocalAccounts.CountAsync());
    }

    [FunctionalTheory]
    [InlineData("", Secret, "DisplayName")]
    [InlineData("Local User", "short", "Secret")]
    public async Task GivenInvalidNameOrSecret_WhenCreatingAccount_ThenBadRequestNamesField(
        string displayName,
        string secret,
        string expectedField)
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var command = ValidCommand();
        command.DisplayName = displayName;
        command.Secret = secret;

        var response = await client.PostAsJsonAsync("/api/local-accounts", command);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedField, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Equal(0, await context.LocalAccounts.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenOperatingSystemStoreUnavailable_WhenCreatingAccount_ThenInMemoryModeIsOffered()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var command = ValidCommand();
        command.StorageMode = LocalAccountStorageMode.OperatingSystem;

        var response = await client.PostAsJsonAsync("/api/local-accounts", command);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("InMemory", body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Equal(0, await context.LocalAccounts.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenMatchingLocalCredentials_WhenAuthenticating_ThenLocalTokenAccessesExistingProfile()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        var creation = await client.PostAsJsonAsync("/api/local-accounts", ValidCommand());
        var created = await creation.Content.ReadFromJsonAsync<CreationEnvelope>();

        var response = await client.PostAsJsonAsync("/api/local-accounts/authenticate", ValidLogin());
        var authenticated = await response.Content.ReadFromJsonAsync<AuthenticationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(authenticated?.Data);
        Assert.NotEmpty(authenticated.Data.Token);
        Assert.True(authenticated.Data.ExpiresAt > DateTimeOffset.UtcNow);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(authenticated.Data.Token);
        Assert.Equal("heimdall-tests", token.Issuer);
        Assert.Contains("fortuna-tests", token.Audiences);
        Assert.Equal(
            created!.Data!.UserId.ToString(),
            token.Claims.Single(claim => claim.Type == FortunaIdentityMapper.SubjectClaim).Value);
        Assert.Equal(
            ((int)HeimdallRoles.User).ToString(),
            token.Claims.Single(claim => claim.Type == FortunaIdentityMapper.RoleClaim).Value);
        Assert.Equal(
            "Local User",
            token.Claims.Single(claim => claim.Type == FortunaIdentityMapper.DisplayNameClaim).Value);
        Assert.Equal(
            bool.TrueString,
            token.Claims.Single(claim => claim.Type == FortunaIdentityMapper.LocalIdentityClaim).Value);
        Assert.InRange(
            Math.Abs((authenticated.Data.ExpiresAt.UtcDateTime - token.ValidTo).TotalSeconds),
            0,
            2);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticated.Data.Token);
        var profile = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");
        Assert.Equal(created.Data.UserId, profile!.Data!.Id);
        Assert.Equal("Local User", profile.Data.DisplayName);
        await using var context = CreateContext();
        Assert.Equal(1, await context.UserProfiles.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenWrongSecretOrUnknownName_WhenAuthenticating_ThenUnauthorizedResponsesMatch()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();
        _ = await client.PostAsJsonAsync("/api/local-accounts", ValidCommand());

        var wrong = await client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            name = "Local User",
            secret = "wrong-secret"
        });
        var unknown = await client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            name = "Unknown User",
            secret = Secret
        });
        var wrongBody = await wrong.Content.ReadFromJsonAsync<ErrorEnvelope>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        Assert.Equal(wrongBody!.Errors.ToArray(), unknownBody!.Errors.ToArray());
        Assert.Contains(LocalAuthenticationMessages.InvalidCredentials, wrongBody.Errors);
        await using var context = CreateContext();
        Assert.Equal(1, await context.LocalAccounts.CountAsync());
        Assert.Equal(1, await context.UserProfiles.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenNoLocalAccount_WhenAuthenticating_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/local-accounts/authenticate", ValidLogin());
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(LocalAuthenticationMessages.InvalidCredentials, body!.Errors);
        await using var context = CreateContext();
        Assert.Equal(0, await context.LocalAccounts.CountAsync());
        Assert.Equal(0, await context.UserProfiles.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenLocalAuthenticationDisabled_WhenAuthenticating_ThenEndpointIsHidden()
    {
        await using var factory = CreateFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/local-accounts/authenticate", ValidLogin());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenPasswordResetRequested_WhenLocalModeIsEnabled_ThenRecoveryCodesAreDirected()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/local-accounts/password-reset", null);
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(LocalAuthenticationMessages.PasswordResetUnavailable, body!.Errors);
        Assert.Contains("recovery", body.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [FunctionalFact]
    public async Task GivenPasswordResetRequested_WhenLocalModeIsDisabled_ThenEndpointIsHidden()
    {
        await using var factory = CreateFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/local-accounts/password-reset", null);

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

    private static CreateLocalAccountCommand ValidCommand() => new()
    {
        DisplayName = "Local User",
        Secret = Secret,
        StorageMode = LocalAccountStorageMode.InMemory
    };

    private static object ValidLogin() => new
    {
        name = "Local User",
        secret = Secret
    };

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
    private sealed record CreationData(
        Guid Id,
        Guid UserId,
        string DisplayName,
        LocalAccountStorageMode StorageMode,
        IReadOnlyCollection<string> RecoveryCodes,
        string RecoveryWarning);
    private sealed record AuthenticationEnvelope(AuthenticationData? Data);
    private sealed record AuthenticationData(string Token, DateTimeOffset ExpiresAt);
    private sealed record ProfileEnvelope(ProfileData? Data);
    private sealed record ProfileData(Guid Id, string DisplayName);
    private sealed record ErrorEnvelope(IReadOnlyCollection<string> Errors);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(byte[] value) => value.Aggregate(17, (hash, item) => hash * 31 + item);
    }
}
