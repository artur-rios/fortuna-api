using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class HeimdallAuthCommandHandlerTests
{
    private static readonly Guid ScopeId = Guid.Parse("00000000-0000-0000-0000-000000000076");
    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.Parse("2026-09-09T13:00:00Z");

    [UnitFact]
    public async Task GivenValidCredentials_WhenLoggingIn_ThenConfiguredScopeAndTokenAreReturned()
    {
        var gateway = new StubGateway
        {
            LoginResult = new(HeimdallAuthOutcome.Succeeded,
                new("token", ExpiresAt, true, false, null, null))
        };
        var handler = new LoginThroughApiCommandHandler(
            new LoginThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new()
        {
            Email = " user@example.test ",
            Password = "do-not-log-this"
        });

        Assert.Equal("token", result.Data?.Token);
        Assert.Equal(ExpiresAt, result.Data?.ExpiresAt);
        Assert.Equal(("user@example.test", "do-not-log-this", ScopeId), gateway.LoginRequest);
        Assert.Contains(HeimdallAuthMessages.AuthenticatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenTwoFactorAccount_WhenLoggingIn_ThenChallengeIsReturnedWithoutToken()
    {
        var gateway = new StubGateway
        {
            LoginResult = new(HeimdallAuthOutcome.Succeeded,
                new(null, null, null, true, "challenge", ["App", "Email"]))
        };
        var handler = new LoginThroughApiCommandHandler(
            new LoginThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new()
        {
            Email = "user@example.test",
            Password = "secret"
        });

        Assert.True(result.Data?.RequiresTwoFactor);
        Assert.Equal("challenge", result.Data?.ChallengeToken);
        Assert.Null(result.Data?.Token);
        Assert.Equal(["App", "Email"], result.Data?.AvailableMethods);
    }

    [UnitTheory]
    [InlineData(HeimdallAuthOutcome.Rejected, HeimdallAuthMessages.AuthenticationRejected)]
    [InlineData(HeimdallAuthOutcome.InvalidRequest, HeimdallAuthMessages.AuthenticationRejected)]
    [InlineData(HeimdallAuthOutcome.Unavailable, HeimdallAuthMessages.ServiceUnavailable)]
    public async Task GivenUpstreamFailure_WhenLoggingIn_ThenSafeFailureIsReturned(
        HeimdallAuthOutcome outcome,
        string expected)
    {
        var gateway = new StubGateway { LoginResult = new(outcome) };
        var handler = new LoginThroughApiCommandHandler(
            new LoginThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new()
        {
            Email = "user@example.test",
            Password = "secret"
        });

        Assert.Contains(expected, result.Errors);
        Assert.DoesNotContain("secret", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("", "secret", HeimdallAuthMessages.EmailRequired)]
    [InlineData("invalid", "secret", HeimdallAuthMessages.EmailInvalid)]
    [InlineData("user@example.test", "", HeimdallAuthMessages.PasswordRequired)]
    public async Task GivenMalformedLogin_WhenLoggingIn_ThenValidationFailsWithoutCallingHeimdall(
        string email,
        string password,
        string expected)
    {
        var gateway = new StubGateway();
        var handler = new LoginThroughApiCommandHandler(
            new LoginThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new() { Email = email, Password = password });

        Assert.Contains(expected, result.Errors);
        Assert.Null(gateway.LoginRequest);
    }

    [UnitFact]
    public async Task GivenValidGoogleToken_WhenSigningIn_ThenConfiguredScopeAndTokenAreReturned()
    {
        var gateway = new StubGateway
        {
            GoogleResult = new(HeimdallAuthOutcome.Succeeded,
                new("google-token", ExpiresAt, true))
        };
        var handler = new GoogleSignInThroughApiCommandHandler(
            new GoogleSignInThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new() { IdToken = "google-id-token" });

        Assert.Equal("google-token", result.Data?.Token);
        Assert.Equal(("google-id-token", ScopeId), gateway.GoogleRequest);
    }

    [UnitFact]
    public async Task GivenValidSecondFactor_WhenVerifying_ThenFullTokenIsReturned()
    {
        var gateway = new StubGateway
        {
            TwoFactorResult = new(HeimdallAuthOutcome.Succeeded,
                new("full-token", ExpiresAt, true))
        };
        var handler = new VerifyTwoFactorThroughApiCommandHandler(
            new VerifyTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            ChallengeToken = "challenge",
            RecoveryCode = "recovery"
        });

        Assert.Equal("full-token", result.Data?.Token);
        Assert.Equal(("challenge", null, "recovery"), gateway.TwoFactorRequest);
    }

    [UnitTheory]
    [InlineData("", null, null, HeimdallAuthMessages.ChallengeTokenRequired)]
    [InlineData("challenge", null, null, HeimdallAuthMessages.FactorRequired)]
    [InlineData("challenge", "123456", "recovery", HeimdallAuthMessages.FactorAmbiguous)]
    public async Task GivenMalformedSecondFactor_WhenVerifying_ThenValidationFails(
        string challenge,
        string? code,
        string? recoveryCode,
        string expected)
    {
        var gateway = new StubGateway();
        var handler = new VerifyTwoFactorThroughApiCommandHandler(
            new VerifyTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            ChallengeToken = challenge,
            Code = code,
            RecoveryCode = recoveryCode
        });

        Assert.Contains(expected, result.Errors);
        Assert.Null(gateway.TwoFactorRequest);
    }

    [UnitTheory]
    [InlineData(HeimdallAuthOutcome.Succeeded)]
    [InlineData(HeimdallAuthOutcome.Rejected)]
    [InlineData(HeimdallAuthOutcome.NotFound)]
    [InlineData(HeimdallAuthOutcome.InvalidRequest)]
    public async Task GivenAnyChallengeOutcome_WhenResendingTheCode_ThenTheAnswerIsAlwaysTheSame(
        HeimdallAuthOutcome outcome)
    {
        // Given — a valid challenge, an unknown one, an expired one and one with no email
        // method are exactly the cases Heimdall reports through these outcomes.
        var gateway = new StubGateway
        {
            ResendResult = new(outcome, outcome == HeimdallAuthOutcome.Succeeded ? new object() : null)
        };
        var handler = new ResendTwoFactorChallengeCodeThroughApiCommandHandler(
            new ResendTwoFactorChallengeCodeThroughApiCommandValidator(), gateway);

        // When
        var result = await handler.HandleAsync(new() { ChallengeToken = "challenge" });

        // Then — nothing here may vary with the outcome, or it becomes an oracle for whether
        // an address is registered and has email two-factor enabled.
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Equal([HeimdallAuthMessages.ChallengeCodeResent], result.Messages);
        Assert.Equal("challenge", gateway.ResendChallengeToken);
    }

    [UnitFact]
    public async Task GivenIdentityServiceIsDown_WhenResendingTheCode_ThenUnavailableIsReported()
    {
        // Given — unavailability says nothing about any account, so unlike every other
        // outcome it is reported rather than collapsed.
        var gateway = new StubGateway
        {
            ResendResult = new(HeimdallAuthOutcome.Unavailable)
        };
        var handler = new ResendTwoFactorChallengeCodeThroughApiCommandHandler(
            new ResendTwoFactorChallengeCodeThroughApiCommandValidator(), gateway);

        // When
        var result = await handler.HandleAsync(new() { ChallengeToken = "challenge" });

        // Then
        Assert.False(result.Success);
        Assert.Contains(HeimdallAuthMessages.ServiceUnavailable, result.Errors);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenNoChallengeToken_WhenResendingTheCode_ThenHeimdallIsNotCalled()
    {
        // Given
        var gateway = new StubGateway();
        var handler = new ResendTwoFactorChallengeCodeThroughApiCommandHandler(
            new ResendTwoFactorChallengeCodeThroughApiCommandValidator(), gateway);

        // When
        var result = await handler.HandleAsync(new() { ChallengeToken = "  " });

        // Then
        Assert.Contains(HeimdallAuthMessages.ChallengeTokenRequired, result.Errors);
        Assert.Equal(0, gateway.ResendCalls);
    }

    [UnitFact]
    public async Task GivenAuthenticatedGoogleSession_WhenSigningOut_ThenBearerTokenIsForwarded()
    {
        var gateway = new StubGateway
        {
            SignOutResult = new(HeimdallAuthOutcome.Succeeded, new object())
        };
        var handler = new GoogleSignOutThroughApiCommandHandler(gateway);

        var result = await handler.HandleAsync(new() { BearerToken = "bearer-token" });

        Assert.Equal("bearer-token", gateway.SignOutToken);
        Assert.NotNull(result.Data);
        Assert.Contains(HeimdallAuthMessages.SignedOutSuccessfully, result.Messages);
    }

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
        public (string Email, string Password, Guid ScopeId)? LoginRequest { get; private set; }
        public (string IdToken, Guid ScopeId)? GoogleRequest { get; private set; }
        public (string Challenge, string? Code, string? RecoveryCode)? TwoFactorRequest { get; private set; }
        public string? SignOutToken { get; private set; }
        public HeimdallAuthResult<object> ResendResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public string? ResendChallengeToken { get; private set; }
        public int ResendCalls { get; private set; }

        public Task<HeimdallAuthResult<object>> ResendTwoFactorChallengeCodeAsync(
            string challengeToken, CancellationToken cancellationToken)
        {
            ResendChallengeToken = challengeToken;
            ResendCalls++;
            return Task.FromResult(ResendResult);
        }

        public Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
            string email, string password, Guid scopeId, CancellationToken cancellationToken)
        {
            LoginRequest = (email, password, scopeId);
            return Task.FromResult(LoginResult);
        }

        public Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
            string idToken, Guid scopeId, CancellationToken cancellationToken)
        {
            GoogleRequest = (idToken, scopeId);
            return Task.FromResult(GoogleResult);
        }

        public Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
            string challengeToken, string? code, string? recoveryCode,
            CancellationToken cancellationToken)
        {
            TwoFactorRequest = (challengeToken, code, recoveryCode);
            return Task.FromResult(TwoFactorResult);
        }

        public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
            string bearerToken, CancellationToken cancellationToken)
        {
            SignOutToken = bearerToken;
            return Task.FromResult(SignOutResult);
        }

        public Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
            string email, Guid scopeId, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<object>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<object>> ResetPasswordAsync(
            string token, string newPassword, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<object>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<object>> VerifyEmailAsync(
            string token, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<object>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<object>> ResendVerificationAsync(
            string bearerToken, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<object>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallTwoFactorStatusResult>> GetTwoFactorStatusAsync(
            string bearerToken, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallTwoFactorStatusResult>(
                HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallTwoFactorSetupResult>> EnableTwoFactorAsync(
            IReadOnlyCollection<string> methods, string bearerToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallTwoFactorSetupResult>(
                HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> ConfirmTwoFactorAsync(
            string? appCode, string? emailCode, string bearerToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallRecoveryCodesResult>(
                HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallTwoFactorDisabledResult>> DisableTwoFactorAsync(
            string password, string? code, string? recoveryCode, string bearerToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallTwoFactorDisabledResult>(
                HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> RegenerateRecoveryCodesAsync(
            string? code, string? recoveryCode, string bearerToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallRecoveryCodesResult>(
                HeimdallAuthOutcome.Rejected));
    }
}
