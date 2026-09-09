using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class HeimdallAuthGatewayTests
{
    private static readonly Guid ScopeId = Guid.Parse("00000000-0000-0000-0000-000000000076");

    [UnitFact]
    public async Task GivenLoginRequest_WhenForwarded_ThenScopeIsAttachedAndResponseIsMapped()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"data":{"token":"issued","expiresAt":"2026-09-09T13:00:00Z","emailVerified":true,"requiresTwoFactor":false}}
            """));
        var gateway = Gateway(handler);

        var result = await gateway.LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal("issued", result.Data?.Token);
        Assert.Contains($"\"scopeId\":\"{ScopeId}\"", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"email\":\"user@example.test\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"credential\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("/api/auth/login", handler.Path);
    }

    [UnitFact]
    public async Task GivenTwoFactorChallenge_WhenForwarded_ThenChallengeShapeIsPreserved()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"data":{"token":null,"expiresAt":null,"emailVerified":null,"requiresTwoFactor":true,"challengeToken":"challenge","availableMethods":["App"]}}
            """));
        var gateway = Gateway(handler);

        var result = await gateway.LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.True(result.Data?.RequiresTwoFactor);
        Assert.Equal("challenge", result.Data?.ChallengeToken);
        Assert.Equal(["App"], result.Data?.AvailableMethods);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.BadRequest, HeimdallAuthOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, HeimdallAuthOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, HeimdallAuthOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, HeimdallAuthOutcome.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, HeimdallAuthOutcome.Unavailable)]
    public async Task GivenUpstreamFailure_WhenForwarded_ThenOnlySafeOutcomeIsReturned(
        HttpStatusCode status,
        HeimdallAuthOutcome expected)
    {
        var handler = new RecordingHandler(_ => Json(status,
            "{\"errors\":[\"sensitive upstream detail\"]}"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenHeimdallIsUnreachable_WhenForwarded_ThenUnavailableIsReturned()
    {
        var gateway = Gateway(new ThrowingHandler());

        var result = await gateway.GoogleSignInAsync(
            "id-token", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Unavailable, result.Outcome);
    }

    [UnitFact]
    public async Task GivenAuthenticatedSignOut_WhenForwarded_ThenBearerTokenIsCarriedOnlyInHeader()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));

        var result = await Gateway(handler).GoogleSignOutAsync(
            "sensitive-token", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sensitive-token"), handler.Authorization);
        Assert.DoesNotContain("sensitive-token", handler.Body, StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenPasswordRecovery_WhenForwarded_ThenConfiguredScopeIsAttached()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));

        var result = await Gateway(handler).RequestPasswordRecoveryAsync(
            "user@example.test", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal("/api/auth/password-recovery", handler.Path);
        Assert.Contains($"\"scopeId\":\"{ScopeId}\"", handler.Body,
            StringComparison.OrdinalIgnoreCase);
    }

    [UnitFact]
    public async Task GivenPasswordReset_WhenForwarded_ThenExactRequestShapeIsUsed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));

        await Gateway(handler).ResetPasswordAsync(
            "reset-token", "new-password", CancellationToken.None);

        Assert.Equal("/api/auth/password-reset", handler.Path);
        Assert.Contains("\"token\":\"reset-token\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"newPassword\":\"new-password\"", handler.Body, StringComparison.Ordinal);
        Assert.Null(handler.Authorization);
    }

    [UnitFact]
    public async Task GivenAuthenticatedStatusRequest_WhenForwarded_ThenGetAndBearerAreUsed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"isActive\":true,\"appEnabled\":true,\"emailEnabled\":false,\"remainingRecoveryCodes\":7}}"));

        var result = await Gateway(handler).GetTwoFactorStatusAsync(
            "bearer-token", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/auth/2fa", handler.Path);
        Assert.Equal(7, result.Data?.RemainingRecoveryCodes);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "bearer-token"), handler.Authorization);
        Assert.Empty(handler.Body);
    }

    [UnitFact]
    public async Task GivenTwoFactorSetup_WhenForwarded_ThenSetupPayloadIsPreserved()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"otpAuthUri\":\"otpauth://totp/Fortuna\",\"emailCodeSent\":true}}"));

        var result = await Gateway(handler).EnableTwoFactorAsync(
            ["App", "Email"], "bearer-token", CancellationToken.None);

        Assert.Equal("otpauth://totp/Fortuna", result.Data?.OtpAuthUri);
        Assert.Contains("\"methods\":[\"App\",\"Email\"]", handler.Body, StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenTwoFactorConfirmation_WhenForwarded_ThenOneTimeCodesArePreserved()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"enabled\":true,\"recoveryCodes\":[\"one\",\"two\"]}}"));

        var result = await Gateway(handler).ConfirmTwoFactorAsync(
            "123456", null, "bearer-token", CancellationToken.None);

        Assert.True(result.Data?.Enabled);
        Assert.Equal(["one", "two"], result.Data?.RecoveryCodes);
        Assert.Contains("\"appCode\":\"123456\"", handler.Body, StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenTwoFactorDisable_WhenForwarded_ThenSecretsAreOnlyInTlsRequest()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"disabled\":true}}"));

        var result = await Gateway(handler).DisableTwoFactorAsync(
            "password", null, "recovery-code", "bearer-token", CancellationToken.None);

        Assert.True(result.Data?.Disabled);
        Assert.Contains("\"password\":\"password\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"recoveryCode\":\"recovery-code\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "bearer-token"), handler.Authorization);
    }

    [UnitFact]
    public async Task GivenRecoveryCodeRegeneration_WhenForwarded_ThenCodesArePreserved()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"data\":{\"recoveryCodes\":[\"new-code\"]}}"));

        var result = await Gateway(handler).RegenerateRecoveryCodesAsync(
            "123456", null, "bearer-token", CancellationToken.None);

        Assert.Equal(["new-code"], result.Data?.RecoveryCodes);
        Assert.Equal("/api/auth/2fa/recovery-codes/regenerate", handler.Path);
    }

    [UnitFact]
    public async Task GivenUpstreamFailure_WhenCredentialSent_ThenLogContainsNoSecretOrResponseBody()
    {
        var logger = new ListLogger();
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.InternalServerError,
            "{\"errors\":[\"upstream-sensitive-detail\"]}"));
        var gateway = new HeimdallAuthGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://heimdall.example.test/") },
            logger);

        await gateway.ResetPasswordAsync(
            "reset-token", "new-password", CancellationToken.None);
        var log = string.Join(' ', logger.Messages);

        Assert.DoesNotContain("reset-token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("new-password", log, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream-sensitive-detail", log, StringComparison.Ordinal);
        Assert.Contains("500", log, StringComparison.Ordinal);
    }

    private static HeimdallAuthGateway Gateway(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://heimdall.example.test/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public string Path { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Method = request.Method;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization;
            return response(request);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class ListLogger : ILogger<HeimdallAuthGateway>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
