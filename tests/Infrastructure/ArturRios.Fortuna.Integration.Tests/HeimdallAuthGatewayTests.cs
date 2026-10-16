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
    [InlineData(HttpStatusCode.UnprocessableEntity, HeimdallAuthOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, HeimdallAuthOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, HeimdallAuthOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HeimdallAuthOutcome.Unavailable)]
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
        Assert.Null(result.Message);
        Assert.Null(result.RetryAfterSeconds);
    }

    [UnitFact]
    public async Task GivenHeimdallRateLimitsWithRetryAfter_WhenForwarded_ThenItsMessageAndWaitAreCarried()
    {
        var handler = new RecordingHandler(_ => WithRetryAfter(
            Json(HttpStatusCode.TooManyRequests, "{\"errors\":[\"Too many requests.\"]}"), "37"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
        Assert.Equal("Too many requests.", result.Message);
        Assert.Equal(37, result.RetryAfterSeconds);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenHeimdallRateLimitsWithoutRetryAfter_WhenForwarded_ThenOnlyItsMessageIsCarried()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests,
            "{\"errors\":[\"An email of this kind was sent to you recently. Try again in a few minutes.\"]}"));

        var result = await Gateway(handler).ResendVerificationAsync("bearer-token", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
        Assert.Equal(
            "An email of this kind was sent to you recently. Try again in a few minutes.",
            result.Message);
        Assert.Null(result.RetryAfterSeconds);
    }

    [UnitTheory]
    [InlineData("0", 0)]
    [InlineData("86400", 86400)]
    [InlineData("soon", null)]
    [InlineData("-5", null)]
    [InlineData("1.5", null)]
    [InlineData("Wed, 21 Oct 2026 07:28:00 GMT", null)]
    [InlineData("99999999999", null)]
    public async Task GivenARetryAfterValue_WhenHeimdallRateLimits_ThenOnlyValidDeltaSecondsAreCarried(
        string value, int? expected)
    {
        var handler = new RecordingHandler(_ => WithRetryAfter(
            Json(HttpStatusCode.TooManyRequests, "{\"errors\":[\"Too many requests.\"]}"), value));

        var result = await Gateway(handler).RegisterAsync(
            "New Person", "new@example.test", "a-long-secret", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
        Assert.Equal(expected, result.RetryAfterSeconds);
    }

    [UnitTheory]
    [InlineData("Too Many Requests", "text/plain")]
    [InlineData("{}", "application/json")]
    [InlineData("{\"errors\":[]}", "application/json")]
    [InlineData("{\"errors\":[\"  \"]}", "application/json")]
    [InlineData("{\"errors\":", "application/json")]
    public async Task GivenARateLimitWithoutHeimdallsMessage_WhenForwarded_ThenItIsStillRateLimitedWithNoMessage(
        string body, string mediaType)
    {
        var handler = new RecordingHandler(_ => WithRetryAfter(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        }, "12"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal(12, result.RetryAfterSeconds);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.TooManyRequests, HeimdallAuthOutcome.RateLimited, 12)]
    [InlineData(HttpStatusCode.Conflict, HeimdallAuthOutcome.Conflict, null)]
    public async Task GivenARefusalWithAnUnknownCharset_WhenForwarded_ThenItIsRelayedWithNoMessage(
        HttpStatusCode status, HeimdallAuthOutcome expected, int? retryAfterSeconds)
    {
        // A proxy in front of Heimdall can name a charset .NET does not know; reading the body then
        // throws, and the refusal must still reach the caller as if it had no body.
        var content = new ByteArrayContent("<p>Slow down</p>"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=x-bogus");
        var handler = new RecordingHandler(_ => WithRetryAfter(new HttpResponseMessage(status)
        {
            Content = content
        }, "12"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal(retryAfterSeconds, result.RetryAfterSeconds);
    }

    [UnitFact]
    public async Task GivenHeimdallAnswersConflict_WhenForwarded_ThenConflictWithItsMessageIsReturned()
    {
        var handler = new RecordingHandler(_ => WithRetryAfter(Json(HttpStatusCode.Conflict,
            "{\"data\":null,\"messages\":[],\"errors\":[\"Two-factor authentication is already active.\"]}"),
            "30"));

        var result = await Gateway(handler).EnableTwoFactorAsync(
            ["App"], "bearer-token", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Conflict, result.Outcome);
        Assert.Equal("Two-factor authentication is already active.", result.Message);
        Assert.Null(result.RetryAfterSeconds);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenHeimdallAnswersConflictWithoutAMessage_WhenForwarded_ThenConflictIsReturned()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Conflict, "{}"));

        var result = await Gateway(handler).GoogleSignInAsync("id-token", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Conflict, result.Outcome);
        Assert.Null(result.Message);
    }

    [UnitFact]
    public async Task GivenARelayedRefusal_WhenLogged_ThenHeimdallsMessageIsNotWritten()
    {
        var logger = new ListLogger();
        var gateway = new HeimdallAuthGateway(
            new HttpClient(new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests,
                "{\"errors\":[\"relayed-upstream-message\"]}")))
            {
                BaseAddress = new Uri("https://heimdall.example.test/")
            },
            logger);

        await gateway.LoginAsync("user@example.test", "credential", ScopeId, CancellationToken.None);
        var log = string.Join(' ', logger.Messages);

        Assert.DoesNotContain("relayed-upstream-message", log, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", log, StringComparison.Ordinal);
        Assert.Contains("429", log, StringComparison.Ordinal);
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
    public async Task GivenRegistration_WhenForwarded_ThenExactShapeAndConfiguredScopeAreUsed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Accepted, "{\"data\":{}}"));

        var result = await Gateway(handler).RegisterAsync(
            "New Person", "new@example.test", "a-long-secret", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal("/api/auth/register", handler.Path);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("\"name\":\"New Person\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"email\":\"new@example.test\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"a-long-secret\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains($"\"scopeId\":\"{ScopeId}\"", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.Authorization);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.Forbidden, HeimdallAuthOutcome.Closed)]
    [InlineData(HttpStatusCode.BadRequest, HeimdallAuthOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.TooManyRequests, HeimdallAuthOutcome.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, HeimdallAuthOutcome.Unavailable)]
    public async Task GivenHeimdallRefusesARegistration_WhenForwarded_ThenTheOutcomeIsMapped(
        HttpStatusCode status, HeimdallAuthOutcome expected)
    {
        var handler = new RecordingHandler(_ => Json(status, "{\"errors\":[\"upstream\"]}"));

        var result = await Gateway(handler).RegisterAsync(
            "New Person", "new@example.test", "a-long-secret", ScopeId, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [UnitFact]
    public async Task GivenHeimdallRateLimitsALogin_WhenForwarded_ThenRateLimitedIsReturned()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenHeimdallRateLimitsARegistration_WhenForwarded_ThenRateLimitedIsReturned()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));

        var result = await Gateway(handler).RegisterAsync(
            "New Person", "new@example.test", "a-long-secret", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
    }

    [UnitFact]
    public async Task GivenHeimdallRateLimitsAVerification_WhenForwarded_ThenRateLimitedIsReturned()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));

        var result = await Gateway(handler).VerifyEmailAsync("t", "pw", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.RateLimited, result.Outcome);
    }

    [UnitFact]
    public async Task GivenConfirmationPassword_WhenVerifyingEmail_ThenExactShapeIsUsed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));

        var result = await Gateway(handler).VerifyEmailAsync("t", "pw", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal("/api/auth/verify-email", handler.Path);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("{\"token\":\"t\",\"password\":\"pw\"}", handler.Body);
        Assert.Null(handler.Authorization);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.UnprocessableEntity, HeimdallAuthOutcome.PasswordRequired)]
    [InlineData(HttpStatusCode.Unauthorized, HeimdallAuthOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, HeimdallAuthOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, HeimdallAuthOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, HeimdallAuthOutcome.Unavailable)]
    public async Task GivenHeimdallRefusesAVerification_WhenForwarded_ThenTheOutcomeIsMapped(
        HttpStatusCode status, HeimdallAuthOutcome expected)
    {
        var handler = new RecordingHandler(_ => Json(status, "{\"errors\":[\"upstream\"]}"));

        var result = await Gateway(handler).VerifyEmailAsync("t", "pw", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [UnitFact]
    public async Task GivenUnprocessableOnPasswordReset_WhenForwarded_ThenItIsStillUnavailable()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.UnprocessableEntity, "{}"));

        var result = await Gateway(handler).ResetPasswordAsync("t", "new-password", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Unavailable, result.Outcome);
    }

    [UnitFact]
    public async Task GivenAVerificationThatFails_WhenLogged_ThenThePasswordIsNotWritten()
    {
        var logger = new ListLogger();
        var gateway = new HeimdallAuthGateway(
            new HttpClient(new RecordingHandler(_ => Json(HttpStatusCode.InternalServerError, "{}")))
            {
                BaseAddress = new Uri("https://heimdall.example.test/")
            },
            logger);

        await gateway.VerifyEmailAsync("verify-token", "sign-up-secret", CancellationToken.None);
        var log = string.Join(' ', logger.Messages);

        Assert.DoesNotContain("sign-up-secret", log, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-token", log, StringComparison.Ordinal);
        Assert.Contains("500", log, StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenForbiddenOnGoogleSignIn_WhenForwarded_ThenItIsStillRejectedNotClosed()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Forbidden, "{\"errors\":[\"off\"]}"));

        var result = await Gateway(handler).GoogleSignInAsync("id-token", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Rejected, result.Outcome);
    }

    [UnitFact]
    public async Task GivenARegistrationThatFails_WhenLogged_ThenThePasswordIsNotWritten()
    {
        var logger = new ListLogger();
        var gateway = new HeimdallAuthGateway(
            new HttpClient(new RecordingHandler(_ => Json(HttpStatusCode.InternalServerError, "{}")))
            {
                BaseAddress = new Uri("https://heimdall.example.test/")
            },
            logger);

        await gateway.RegisterAsync(
            "New Person", "new@example.test", "a-long-secret", ScopeId, CancellationToken.None);
        var log = string.Join(' ', logger.Messages);

        Assert.DoesNotContain("a-long-secret", log, StringComparison.Ordinal);
        Assert.DoesNotContain("new@example.test", log, StringComparison.Ordinal);
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

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, string value)
    {
        response.Headers.TryAddWithoutValidation("Retry-After", value);

        return response;
    }

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
