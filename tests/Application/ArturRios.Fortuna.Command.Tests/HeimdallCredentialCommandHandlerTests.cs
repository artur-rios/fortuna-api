using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class HeimdallCredentialCommandHandlerTests
{
    private static readonly Guid ScopeId = Guid.Parse("00000000-0000-0000-0000-000000000077");

    [UnitFact]
    public async Task GivenRegisteredOrUnknownAddress_WhenRecoveryRequested_ThenSameSuccessIsReturned()
    {
        var gateway = new StubGateway { EmptyResult = Success() };
        var handler = new RequestPasswordRecoveryThroughApiCommandHandler(
            new RequestPasswordRecoveryThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new() { Email = " user@example.test " });

        Assert.NotNull(result.Data);
        Assert.Equal(("user@example.test", ScopeId), gateway.RecoveryRequest);
        Assert.Contains(HeimdallAuthMessages.PasswordRecoveryRequested, result.Messages);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("not-an-address")]
    public async Task GivenMalformedAddress_WhenRecoveryRequested_ThenHeimdallIsNotCalled(string email)
    {
        var gateway = new StubGateway();
        var handler = new RequestPasswordRecoveryThroughApiCommandHandler(
            new RequestPasswordRecoveryThroughApiCommandValidator(), gateway, new(ScopeId));

        var result = await handler.HandleAsync(new() { Email = email });

        Assert.NotEmpty(result.Errors);
        Assert.Null(gateway.RecoveryRequest);
    }

    [UnitFact]
    public async Task GivenValidResetToken_WhenPasswordReset_ThenCredentialIsForwardedOnlyToGateway()
    {
        var gateway = new StubGateway { EmptyResult = Success() };
        var handler = new ResetPasswordThroughApiCommandHandler(
            new ResetPasswordThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            Token = "reset-token",
            NewPassword = "new-secret"
        });

        Assert.NotNull(result.Data);
        Assert.Equal(("reset-token", "new-secret"), gateway.ResetRequest);
        Assert.DoesNotContain("new-secret", string.Join(' ', result.Messages), StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("", "secret")]
    [InlineData("token", "")]
    public async Task GivenMalformedReset_WhenPasswordReset_ThenValidationFails(
        string token, string password)
    {
        var gateway = new StubGateway();
        var handler = new ResetPasswordThroughApiCommandHandler(
            new ResetPasswordThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Token = token, NewPassword = password });

        Assert.NotEmpty(result.Errors);
        Assert.Null(gateway.ResetRequest);
    }

    [UnitFact]
    public async Task GivenInvalidOrExpiredToken_WhenPasswordReset_ThenSafeBadRequestIsReturned()
    {
        var gateway = new StubGateway
        {
            EmptyResult = new(HeimdallAuthOutcome.InvalidRequest)
        };
        var handler = new ResetPasswordThroughApiCommandHandler(
            new ResetPasswordThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Token = "expired", NewPassword = "secret" });

        Assert.Contains(HeimdallAuthMessages.RequestRejected, result.Errors);
        Assert.DoesNotContain("expired", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenVerificationToken_WhenVerifyingEmail_ThenTokenIsForwarded()
    {
        var gateway = new StubGateway { EmptyResult = Success() };
        var handler = new VerifyEmailThroughApiCommandHandler(
            new VerifyEmailThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Token = "verification-token" });

        Assert.NotNull(result.Data);
        Assert.Equal("verification-token", gateway.VerificationToken);
    }

    [UnitFact]
    public async Task GivenAuthenticatedCaller_WhenResendingVerification_ThenBearerIsForwarded()
    {
        var gateway = new StubGateway { EmptyResult = Success() };
        var result = await new ResendVerificationThroughApiCommandHandler(gateway)
            .HandleAsync(new() { BearerToken = "bearer-token" });

        Assert.NotNull(result.Data);
        Assert.Equal("bearer-token", gateway.BearerToken);
    }

    [UnitFact]
    public async Task GivenTwoFactorConfiguration_WhenStatusRequested_ThenNonSecretStateIsReturned()
    {
        var gateway = new StubGateway
        {
            StatusResult = new(HeimdallAuthOutcome.Succeeded, new(true, true, false, 8))
        };

        var result = await new GetTwoFactorStatusThroughApiCommandHandler(gateway)
            .HandleAsync(new() { BearerToken = "bearer-token" });

        Assert.True(result.Data?.IsActive);
        Assert.Equal(8, result.Data?.RemainingRecoveryCodes);
        Assert.Equal("bearer-token", gateway.BearerToken);
    }

    [UnitFact]
    public async Task GivenValidMethods_WhenEnablingTwoFactor_ThenSetupPayloadIsReturned()
    {
        var gateway = new StubGateway
        {
            SetupResult = new(HeimdallAuthOutcome.Succeeded,
                new("otpauth://totp/Fortuna", true))
        };
        var handler = new EnableTwoFactorThroughApiCommandHandler(
            new EnableTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            BearerToken = "bearer-token",
            Methods = ["App", "Email"]
        });

        Assert.Equal("otpauth://totp/Fortuna", result.Data?.OtpAuthUri);
        Assert.Equal(["App", "Email"], gateway.Methods);
    }

    [UnitTheory]
    [InlineData()]
    [InlineData("Sms")]
    public async Task GivenInvalidMethods_WhenEnablingTwoFactor_ThenValidationFails(
        params string[] methods)
    {
        var gateway = new StubGateway();
        var handler = new EnableTwoFactorThroughApiCommandHandler(
            new EnableTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Methods = [.. methods] });

        Assert.NotEmpty(result.Errors);
        Assert.Null(gateway.Methods);
    }

    [UnitFact]
    public async Task GivenFirstValidCode_WhenConfirmingTwoFactor_ThenRecoveryCodesAreReturnedOnce()
    {
        var gateway = new StubGateway
        {
            RecoveryCodesResult = new(HeimdallAuthOutcome.Succeeded,
                new(true, ["one-time-1", "one-time-2"]))
        };
        var handler = new ConfirmTwoFactorThroughApiCommandHandler(
            new ConfirmTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            BearerToken = "bearer-token",
            AppCode = "123456"
        });

        Assert.True(result.Data?.Enabled);
        Assert.Equal(["one-time-1", "one-time-2"], result.Data?.RecoveryCodes);
        Assert.Equal(("123456", null), gateway.ConfirmationCodes);
    }

    [UnitFact]
    public async Task GivenWrongConfirmationCode_WhenConfirmingTwoFactor_ThenSafeRejectionIsReturned()
    {
        var gateway = new StubGateway
        {
            RecoveryCodesResult = new(HeimdallAuthOutcome.Rejected)
        };
        var handler = new ConfirmTwoFactorThroughApiCommandHandler(
            new ConfirmTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { AppCode = "wrong" });

        Assert.Contains(HeimdallAuthMessages.TwoFactorRejected, result.Errors);
        Assert.DoesNotContain("wrong", string.Join(' ', result.Errors), StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenNoActiveSetup_WhenDisablingTwoFactor_ThenNotFoundIsReturned()
    {
        var gateway = new StubGateway
        {
            DisabledResult = new(HeimdallAuthOutcome.NotFound)
        };
        var handler = new DisableTwoFactorThroughApiCommandHandler(
            new DisableTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            Password = "secret",
            Code = "123456"
        });

        Assert.Contains(HeimdallAuthMessages.TwoFactorNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenPasswordAndValidFactor_WhenDisablingTwoFactor_ThenSuccessIsReturned()
    {
        var gateway = new StubGateway
        {
            DisabledResult = new(HeimdallAuthOutcome.Succeeded, new(true))
        };
        var handler = new DisableTwoFactorThroughApiCommandHandler(
            new DisableTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            BearerToken = "bearer-token",
            Password = "secret",
            RecoveryCode = "recovery"
        });

        Assert.True(result.Data?.Disabled);
        Assert.Equal(("secret", null, "recovery"), gateway.DisableRequest);
        Assert.DoesNotContain("secret", string.Join(' ', result.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain("recovery", string.Join(' ', result.Messages), StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("", "123456", null)]
    [InlineData("secret", null, null)]
    [InlineData("secret", "123456", "recovery")]
    public async Task GivenMalformedDisable_WhenDisablingTwoFactor_ThenValidationFails(
        string password, string? code, string? recoveryCode)
    {
        var gateway = new StubGateway();
        var handler = new DisableTwoFactorThroughApiCommandHandler(
            new DisableTwoFactorThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            Password = password,
            Code = code,
            RecoveryCode = recoveryCode
        });

        Assert.NotEmpty(result.Errors);
        Assert.Null(gateway.DisableRequest);
    }

    [UnitFact]
    public async Task GivenValidFactor_WhenRegeneratingCodes_ThenNewCodesAreReturned()
    {
        var gateway = new StubGateway
        {
            RecoveryCodesResult = new(HeimdallAuthOutcome.Succeeded,
                new(null, ["new-code"]))
        };
        var handler = new RegenerateRecoveryCodesThroughApiCommandHandler(
            new RegenerateRecoveryCodesThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new()
        {
            BearerToken = "bearer-token",
            RecoveryCode = "old-code"
        });

        Assert.Equal(["new-code"], result.Data?.RecoveryCodes);
        Assert.Equal((null, "old-code"), gateway.RegenerationFactor);
    }

    [UnitFact]
    public async Task GivenNoActiveSetup_WhenRegeneratingCodes_ThenNotFoundIsReturned()
    {
        var gateway = new StubGateway
        {
            RecoveryCodesResult = new(HeimdallAuthOutcome.NotFound)
        };
        var handler = new RegenerateRecoveryCodesThroughApiCommandHandler(
            new RegenerateRecoveryCodesThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Code = "123456" });

        Assert.Contains(HeimdallAuthMessages.TwoFactorNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenHeimdallUnavailable_WhenManagingCredential_ThenSafeUnavailableIsReturned()
    {
        var gateway = new StubGateway
        {
            EmptyResult = new(HeimdallAuthOutcome.Unavailable)
        };
        var handler = new VerifyEmailThroughApiCommandHandler(
            new VerifyEmailThroughApiCommandValidator(), gateway);

        var result = await handler.HandleAsync(new() { Token = "sensitive-token" });

        Assert.Contains(HeimdallAuthMessages.ServiceUnavailable, result.Errors);
        Assert.DoesNotContain("sensitive-token", string.Join(' ', result.Errors),
            StringComparison.Ordinal);
    }

    private static HeimdallAuthResult<object> Success() =>
        new(HeimdallAuthOutcome.Succeeded, new object());

    private sealed class StubGateway : IHeimdallAuthGateway
    {
        public HeimdallAuthResult<object> EmptyResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallTwoFactorStatusResult> StatusResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallTwoFactorSetupResult> SetupResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallRecoveryCodesResult> RecoveryCodesResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public HeimdallAuthResult<HeimdallTwoFactorDisabledResult> DisabledResult { get; init; } =
            new(HeimdallAuthOutcome.Rejected);
        public (string Email, Guid ScopeId)? RecoveryRequest { get; private set; }
        public (string Token, string Password)? ResetRequest { get; private set; }
        public string? VerificationToken { get; private set; }
        public string? BearerToken { get; private set; }
        public IReadOnlyCollection<string>? Methods { get; private set; }
        public (string? AppCode, string? EmailCode)? ConfirmationCodes { get; private set; }
        public (string Password, string? Code, string? RecoveryCode)? DisableRequest { get; private set; }
        public (string? Code, string? RecoveryCode)? RegenerationFactor { get; private set; }

        public Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
            string email, Guid scopeId, CancellationToken cancellationToken)
        {
            RecoveryRequest = (email, scopeId);
            return Task.FromResult(EmptyResult);
        }

        public Task<HeimdallAuthResult<object>> ResetPasswordAsync(
            string token, string newPassword, CancellationToken cancellationToken)
        {
            ResetRequest = (token, newPassword);
            return Task.FromResult(EmptyResult);
        }

        public Task<HeimdallAuthResult<object>> VerifyEmailAsync(
            string token, CancellationToken cancellationToken)
        {
            VerificationToken = token;
            return Task.FromResult(EmptyResult);
        }

        public Task<HeimdallAuthResult<object>> ResendVerificationAsync(
            string bearerToken, CancellationToken cancellationToken)
        {
            BearerToken = bearerToken;
            return Task.FromResult(EmptyResult);
        }

        public Task<HeimdallAuthResult<HeimdallTwoFactorStatusResult>> GetTwoFactorStatusAsync(
            string bearerToken, CancellationToken cancellationToken)
        {
            BearerToken = bearerToken;
            return Task.FromResult(StatusResult);
        }

        public Task<HeimdallAuthResult<HeimdallTwoFactorSetupResult>> EnableTwoFactorAsync(
            IReadOnlyCollection<string> methods, string bearerToken,
            CancellationToken cancellationToken)
        {
            Methods = methods;
            BearerToken = bearerToken;
            return Task.FromResult(SetupResult);
        }

        public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> ConfirmTwoFactorAsync(
            string? appCode, string? emailCode, string bearerToken,
            CancellationToken cancellationToken)
        {
            ConfirmationCodes = (appCode, emailCode);
            BearerToken = bearerToken;
            return Task.FromResult(RecoveryCodesResult);
        }

        public Task<HeimdallAuthResult<HeimdallTwoFactorDisabledResult>> DisableTwoFactorAsync(
            string password, string? code, string? recoveryCode, string bearerToken,
            CancellationToken cancellationToken)
        {
            DisableRequest = (password, code, recoveryCode);
            BearerToken = bearerToken;
            return Task.FromResult(DisabledResult);
        }

        public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> RegenerateRecoveryCodesAsync(
            string? code, string? recoveryCode, string bearerToken,
            CancellationToken cancellationToken)
        {
            RegenerationFactor = (code, recoveryCode);
            BearerToken = bearerToken;
            return Task.FromResult(RecoveryCodesResult);
        }

        public Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
            string email, string password, Guid scopeId, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallLoginResult>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
            string idToken, Guid scopeId, CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallGoogleSignInResult>(HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
            string challengeToken, string? code, string? recoveryCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HeimdallAuthResult<HeimdallTwoFactorVerificationResult>(
                HeimdallAuthOutcome.Rejected));

        public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
            string bearerToken, CancellationToken cancellationToken) => Task.FromResult(EmptyResult);
    }
}
