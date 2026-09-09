using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class HeimdallAuthProxyTests
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.Parse("2026-09-09T13:00:00Z");

    [FunctionalFact]
    public async Task GivenValidCredentials_WhenLoggingIn_ThenTokenShapeIsReturnedAndCredentialIsNotEchoed()
    {
        var gateway = new StubGateway
        {
            LoginResult = new(HeimdallAuthOutcome.Succeeded,
                new("issued", ExpiresAt, true, false, null, null))
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "user@example.test",
            password = "never-return-this"
        });
        var body = await response.Content.ReadAsStringAsync();
        var envelope = await response.Content.ReadFromJsonAsync<LoginEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("issued", envelope?.Data?.Token);
        Assert.Equal(ExpiresAt, envelope?.Data?.ExpiresAt);
        Assert.DoesNotContain("never-return-this", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenTwoFactorAccount_WhenLoggingIn_ThenChallengeIsReturnedInsteadOfToken()
    {
        var gateway = new StubGateway
        {
            LoginResult = new(HeimdallAuthOutcome.Succeeded,
                new(null, null, null, true, "challenge", ["App", "Email"]))
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "user@example.test",
            password = "secret-in-transit"
        });
        var envelope = await response.Content.ReadFromJsonAsync<LoginEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope?.Data?.RequiresTwoFactor);
        Assert.Equal("challenge", envelope?.Data?.ChallengeToken);
        Assert.Null(envelope?.Data?.Token);
    }

    [FunctionalTheory]
    [InlineData(HeimdallAuthOutcome.Rejected, HttpStatusCode.Unauthorized)]
    [InlineData(HeimdallAuthOutcome.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task GivenHeimdallFailure_WhenLoggingIn_ThenSafeStatusAndMessageAreReturned(
        HeimdallAuthOutcome outcome,
        HttpStatusCode expected)
    {
        var gateway = new StubGateway { LoginResult = new(outcome) };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "user@example.test",
            password = "secret-in-transit"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("secret-in-transit", body, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream", body, StringComparison.OrdinalIgnoreCase);
    }

    [FunctionalTheory]
    [InlineData("", "credential", "Email")]
    [InlineData("not-an-email", "credential", "Email")]
    [InlineData("user@example.test", "", "Password")]
    public async Task GivenMalformedLogin_WhenLoggingIn_ThenBadRequestNamesField(
        string email,
        string password,
        string field)
    {
        var gateway = new StubGateway();
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, gateway.LoginCalls);
    }

    [FunctionalFact]
    public async Task GivenValidGoogleIdentity_WhenSigningIn_ThenTokenIsReturned()
    {
        var gateway = new StubGateway
        {
            GoogleResult = new(HeimdallAuthOutcome.Succeeded,
                new("google-token", ExpiresAt, true))
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/google", new
        {
            idToken = "google-id-token"
        });
        var envelope = await response.Content.ReadFromJsonAsync<GoogleEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("google-token", envelope?.Data?.Token);
    }

    [FunctionalFact]
    public async Task GivenValidSecondFactor_WhenVerifying_ThenFullTokenIsReturned()
    {
        var gateway = new StubGateway
        {
            TwoFactorResult = new(HeimdallAuthOutcome.Succeeded,
                new("full-token", ExpiresAt, true))
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/2fa/verify", new
        {
            challengeToken = "challenge",
            code = "123456"
        });
        var envelope = await response.Content.ReadFromJsonAsync<TwoFactorEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("full-token", envelope?.Data?.Token);
    }

    [FunctionalFact]
    public async Task GivenWrongOrSpentSecondFactor_WhenVerifying_ThenUnauthorizedIsReturned()
    {
        var gateway = new StubGateway
        {
            TwoFactorResult = new(HeimdallAuthOutcome.Rejected)
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/2fa/verify", new
        {
            challengeToken = "challenge",
            recoveryCode = "spent-code"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(HeimdallAuthMessages.TwoFactorRejected, body, StringComparison.Ordinal);
        Assert.DoesNotContain("spent-code", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenNoBearerToken_WhenSigningOut_ThenUnauthorizedIsReturnedWithoutCallingHeimdall()
    {
        var gateway = new StubGateway();
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/google/sign-out", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(gateway.SignOutToken);
    }

    [FunctionalFact]
    public async Task GivenValidBearerToken_WhenSigningOut_ThenSameTokenIsForwarded()
    {
        var gateway = new StubGateway
        {
            SignOutResult = new(HeimdallAuthOutcome.Succeeded, new object())
        };
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        var token = Token();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/auth/google/sign-out", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(token, gateway.SignOutToken);
    }

    [FunctionalFact]
    public async Task GivenTooManyAnonymousAttempts_WhenLoggingIn_ThenRateLimitIsReturned()
    {
        var gateway = new StubGateway();
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = "user@example.test",
                password = "credential"
            });
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(StubGateway gateway)
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
                services.RemoveAll<IHeimdallAuthGateway>();
                services.RemoveAll<IUserProfileProvisioner>();
                services.AddSingleton<IHeimdallAuthGateway>(gateway);
                services.AddSingleton<IUserProfileProvisioner, StubUserProfileProvisioner>();
            });
        });
    }

    private static string Token()
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Test User"
        };
        return new JwtHandler().CreateToken(new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity)));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076"
    };

    private sealed class StubGateway : IHeimdallAuthGateway
    {
        public HeimdallAuthResult<HeimdallLoginResult> LoginResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallGoogleSignInResult> GoogleResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallTwoFactorVerificationResult> TwoFactorResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<object> SignOutResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public int LoginCalls { get; private set; }
        public string? SignOutToken { get; private set; }

        public Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
            string email, string password, Guid scopeId, CancellationToken cancellationToken)
        {
            LoginCalls++;
            return Task.FromResult(LoginResult);
        }

        public Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
            string idToken, Guid scopeId, CancellationToken cancellationToken) =>
            Task.FromResult(GoogleResult);

        public Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
            string challengeToken, string? code, string? recoveryCode,
            CancellationToken cancellationToken) => Task.FromResult(TwoFactorResult);

        public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
            string bearerToken, CancellationToken cancellationToken)
        {
            SignOutToken = bearerToken;
            return Task.FromResult(SignOutResult);
        }
    }

    private sealed class StubUserProfileProvisioner : IUserProfileProvisioner
    {
        public Task<UserProfileSnapshot> GetOrCreateAsync(
            Guid externalSubject,
            string displayName,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new UserProfileSnapshot(
                Guid.NewGuid(), externalSubject, displayName, "BRL", false, now, now));
        }
    }

    private sealed record LoginEnvelope(LoginData? Data, IReadOnlyCollection<string> Errors);
    private sealed record LoginData(
        string? Token,
        DateTimeOffset? ExpiresAt,
        bool? EmailVerified,
        bool RequiresTwoFactor,
        string? ChallengeToken,
        IReadOnlyCollection<string>? AvailableMethods);
    private sealed record GoogleEnvelope(GoogleData? Data);
    private sealed record GoogleData(string Token, DateTimeOffset ExpiresAt, bool EmailVerified);
    private sealed record TwoFactorEnvelope(TwoFactorData? Data);
    private sealed record TwoFactorData(string Token, DateTimeOffset ExpiresAt, bool EmailVerified);
}
