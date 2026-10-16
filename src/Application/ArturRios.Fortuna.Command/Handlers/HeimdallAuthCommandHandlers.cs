using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed record HeimdallAuthOptions(Guid ScopeId);

public sealed class LoginThroughApiCommandHandler(
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<LoginThroughApiCommand, LoginThroughApiCommandOutput>
{
    public async Task<DataOutput<LoginThroughApiCommandOutput?>> HandleAsync(
        LoginThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.LoginAsync(
            command.Email.Trim(), command.Password, options.ScopeId, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return Failure<LoginThroughApiCommandOutput>(result,
                HeimdallAuthMessages.AuthenticationRejected);
        }

        var data = result.Data!;

        return DataOutput<LoginThroughApiCommandOutput?>.New.WithData(new()
        {
            Token = data.Token,
            ExpiresAt = data.ExpiresAt,
            EmailVerified = data.EmailVerified,
            RequiresTwoFactor = data.RequiresTwoFactor,
            ChallengeToken = data.ChallengeToken,
            AvailableMethods = data.AvailableMethods
        }).WithMessage(HeimdallAuthMessages.AuthenticatedSuccessfully);
    }

    // An outcome this helper does not know must not become a 500: every route-specific outcome
    // (Closed, PasswordRequired) is mapped by its own handler before the helper is called, so
    // anything else that reaches the default arm is answered as an unavailable identity service.
    // RateLimited and Conflict are known outcomes on every route and are relayed as Heimdall gave them.
    internal static DataOutput<T?> Failure<T>(
        IHeimdallAuthResult result, string rejected, string? notFound = null)
        where T : class => result.Outcome switch
        {
            HeimdallAuthOutcome.InvalidRequest => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Rejected => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Unavailable => DataOutput<T?>.New.WithError(
                HeimdallAuthMessages.ServiceUnavailable),
            HeimdallAuthOutcome.NotFound => DataOutput<T?>.New.WithError(notFound ?? rejected),
            HeimdallAuthOutcome.RateLimited or HeimdallAuthOutcome.Conflict => Relayed<T>(result),
            _ => DataOutput<T?>.New.WithError(HeimdallAuthMessages.ServiceUnavailable)
        };

    // Heimdall's own message, or Fortuna's when Heimdall named none (a proxy in front of it can
    // answer 429 itself); the outcome and the wait travel with it for the controller.
    internal static DataOutput<T?> Relayed<T>(IHeimdallAuthResult result)
        where T : class
    {
        var fallback = result.Outcome == HeimdallAuthOutcome.Conflict
            ? HeimdallAuthMessages.IdentityConflict
            : HeimdallAuthMessages.TooManyAttempts;

        return new HeimdallRelayedRefusal<T?>
        {
            Outcome = result.Outcome,
            RetryAfterSeconds = result.Outcome == HeimdallAuthOutcome.RateLimited
                ? result.RetryAfterSeconds
                : null
        }.WithError(result.Message ?? fallback);
    }
}

public sealed class GoogleSignInThroughApiCommandHandler(
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<GoogleSignInThroughApiCommand, GoogleSignInThroughApiCommandOutput>
{
    public async Task<DataOutput<GoogleSignInThroughApiCommandOutput?>> HandleAsync(
        GoogleSignInThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.GoogleSignInAsync(
            command.IdToken, options.ScopeId, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<GoogleSignInThroughApiCommandOutput>(
                result, HeimdallAuthMessages.AuthenticationRejected);
        }

        return DataOutput<GoogleSignInThroughApiCommandOutput?>.New.WithData(new()
        {
            Token = result.Data!.Token,
            ExpiresAt = result.Data.ExpiresAt,
            EmailVerified = result.Data.EmailVerified
        }).WithMessage(HeimdallAuthMessages.AuthenticatedSuccessfully);
    }
}

public sealed class VerifyTwoFactorThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<VerifyTwoFactorThroughApiCommand, VerifyTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<VerifyTwoFactorThroughApiCommandOutput?>> HandleAsync(
        VerifyTwoFactorThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.VerifyTwoFactorAsync(
            command.ChallengeToken, command.Code, command.RecoveryCode, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<VerifyTwoFactorThroughApiCommandOutput>(
                result, HeimdallAuthMessages.TwoFactorRejected);
        }

        return DataOutput<VerifyTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            Token = result.Data!.Token,
            ExpiresAt = result.Data.ExpiresAt,
            EmailVerified = result.Data.EmailVerified
        }).WithMessage(HeimdallAuthMessages.AuthenticatedSuccessfully);
    }
}

public sealed class GoogleSignOutThroughApiCommandHandler(IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<GoogleSignOutThroughApiCommand, GoogleSignOutThroughApiCommandOutput>
{
    public async Task<DataOutput<GoogleSignOutThroughApiCommandOutput?>> HandleAsync(
        GoogleSignOutThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.GoogleSignOutAsync(command.BearerToken, cancellationToken);

        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<GoogleSignOutThroughApiCommandOutput?>.New
                .WithData(new GoogleSignOutThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.SignedOutSuccessfully)
            : LoginThroughApiCommandHandler.Failure<GoogleSignOutThroughApiCommandOutput>(
                result, HeimdallAuthMessages.AuthenticationRejected);
    }
}

public sealed class RequestPasswordRecoveryThroughApiCommandHandler(
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<RequestPasswordRecoveryThroughApiCommand,
        RequestPasswordRecoveryThroughApiCommandOutput>
{
    public async Task<DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>> HandleAsync(
        RequestPasswordRecoveryThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.RequestPasswordRecoveryAsync(
            command.Email.Trim(), options.ScopeId, cancellationToken);

        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>.New
                .WithData(new RequestPasswordRecoveryThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.PasswordRecoveryRequested)
            : LoginThroughApiCommandHandler.Failure<RequestPasswordRecoveryThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected);
    }
}

/// <summary>
///     Forwards a registration to Heimdall with the configured scope (UC-81). Heimdall answers a free
///     and a taken address alike, and so does this: the only answers that differ are a closed scope,
///     a malformed request and an unreachable Heimdall, none of which says anything about an address.
/// </summary>
public sealed class RegisterThroughApiCommandHandler(
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<RegisterThroughApiCommand, RegisterThroughApiCommandOutput>
{
    public async Task<DataOutput<RegisterThroughApiCommandOutput?>> HandleAsync(
        RegisterThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.RegisterAsync(
            command.Name.Trim(), command.Email.Trim(), command.Password, options.ScopeId, cancellationToken);

        return result.Outcome switch
        {
            HeimdallAuthOutcome.Succeeded => DataOutput<RegisterThroughApiCommandOutput?>.New
                .WithData(new RegisterThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.RegistrationRequested),
            HeimdallAuthOutcome.Closed => DataOutput<RegisterThroughApiCommandOutput?>.New
                .WithError(HeimdallAuthMessages.RegistrationUnavailable),
            _ => LoginThroughApiCommandHandler.Failure<RegisterThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected)
        };
    }
}

/// <summary>
///     Forwards a challenge-token-authorized resend to Heimdall (UC-77). Answers the same way on
///     every path Heimdall can take — a valid challenge, an unknown one, a forged one, an expired
///     one, one naming a person with no email method, and one whose reissues are spent — because a
///     response that varied would tell an anonymous caller whether an address is registered and has
///     email two-factor enabled. Only Heimdall being unreachable or rate-limiting the caller's address
///     is reported differently, and neither says anything about any account.
/// </summary>
public sealed class ResendTwoFactorChallengeCodeThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ResendTwoFactorChallengeCodeThroughApiCommand,
        ResendTwoFactorChallengeCodeThroughApiCommandOutput>
{
    public async Task<DataOutput<ResendTwoFactorChallengeCodeThroughApiCommandOutput?>> HandleAsync(
        ResendTwoFactorChallengeCodeThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.ResendTwoFactorChallengeCodeAsync(
            command.ChallengeToken, cancellationToken);

        // Every Heimdall outcome but unavailability and the rate limit collapses to the one
        // answer, a conflict included: a rejection distinguishable from a success is the oracle
        // this endpoint must not be. Heimdall rate-limits this route by the caller's address, not
        // by an account, so its refusal says nothing about any address and is relayed as it is.
        return result.Outcome switch
        {
            HeimdallAuthOutcome.Unavailable => DataOutput<ResendTwoFactorChallengeCodeThroughApiCommandOutput?>
                .New.WithError(HeimdallAuthMessages.ServiceUnavailable),
            HeimdallAuthOutcome.RateLimited => LoginThroughApiCommandHandler
                .Relayed<ResendTwoFactorChallengeCodeThroughApiCommandOutput>(result),
            _ => DataOutput<ResendTwoFactorChallengeCodeThroughApiCommandOutput?>.New
                .WithData(new ResendTwoFactorChallengeCodeThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.ChallengeCodeResent)
        };
    }
}

public sealed class ResetPasswordThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ResetPasswordThroughApiCommand, ResetPasswordThroughApiCommandOutput>
{
    public async Task<DataOutput<ResetPasswordThroughApiCommandOutput?>> HandleAsync(
        ResetPasswordThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.ResetPasswordAsync(
            command.Token, command.NewPassword, cancellationToken);

        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<ResetPasswordThroughApiCommandOutput?>.New
                .WithData(new ResetPasswordThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.PasswordReset)
            : LoginThroughApiCommandHandler.Failure<ResetPasswordThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class VerifyEmailThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<VerifyEmailThroughApiCommand, VerifyEmailThroughApiCommandOutput>
{
    public async Task<DataOutput<VerifyEmailThroughApiCommandOutput?>> HandleAsync(
        VerifyEmailThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.VerifyEmailAsync(command.Token, command.Password, cancellationToken);

        return result.Outcome switch
        {
            HeimdallAuthOutcome.Succeeded => DataOutput<VerifyEmailThroughApiCommandOutput?>.New
                .WithData(new VerifyEmailThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.EmailVerified),
            HeimdallAuthOutcome.PasswordRequired => DataOutput<VerifyEmailThroughApiCommandOutput?>.New
                .WithError(HeimdallAuthMessages.ConfirmationPasswordRequired),
            // Heimdall's verify-email answers 401 only for a wrong sign-up password.
            HeimdallAuthOutcome.Rejected => DataOutput<VerifyEmailThroughApiCommandOutput?>.New
                .WithError(HeimdallAuthMessages.ConfirmationPasswordIncorrect),
            _ => LoginThroughApiCommandHandler.Failure<VerifyEmailThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected)
        };
    }
}

public sealed class ResendVerificationThroughApiCommandHandler(IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ResendVerificationThroughApiCommand,
        ResendVerificationThroughApiCommandOutput>
{
    public async Task<DataOutput<ResendVerificationThroughApiCommandOutput?>> HandleAsync(
        ResendVerificationThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.ResendVerificationAsync(
            command.BearerToken, cancellationToken);

        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<ResendVerificationThroughApiCommandOutput?>.New
                .WithData(new ResendVerificationThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.VerificationResent)
            : LoginThroughApiCommandHandler.Failure<ResendVerificationThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class GetTwoFactorStatusThroughApiCommandHandler(IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<GetTwoFactorStatusThroughApiCommand,
        GetTwoFactorStatusThroughApiCommandOutput>
{
    public async Task<DataOutput<GetTwoFactorStatusThroughApiCommandOutput?>> HandleAsync(
        GetTwoFactorStatusThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.GetTwoFactorStatusAsync(command.BearerToken, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<GetTwoFactorStatusThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<GetTwoFactorStatusThroughApiCommandOutput?>.New.WithData(new()
        {
            IsActive = result.Data!.IsActive,
            AppEnabled = result.Data.AppEnabled,
            EmailEnabled = result.Data.EmailEnabled,
            RemainingRecoveryCodes = result.Data.RemainingRecoveryCodes
        }).WithMessage(HeimdallAuthMessages.TwoFactorStatusReturned);
    }
}

public sealed class EnableTwoFactorThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<EnableTwoFactorThroughApiCommand, EnableTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<EnableTwoFactorThroughApiCommandOutput?>> HandleAsync(
        EnableTwoFactorThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.EnableTwoFactorAsync(
            command.Methods, command.BearerToken, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<EnableTwoFactorThroughApiCommandOutput>(
                result, HeimdallAuthMessages.RequestRejected);
        }

        return DataOutput<EnableTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            OtpAuthUri = result.Data!.OtpAuthUri,
            EmailCodeSent = result.Data.EmailCodeSent
        }).WithMessage(HeimdallAuthMessages.TwoFactorSetupStarted);
    }
}

public sealed class ConfirmTwoFactorThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ConfirmTwoFactorThroughApiCommand, ConfirmTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>> HandleAsync(
        ConfirmTwoFactorThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.ConfirmTwoFactorAsync(
            command.AppCode, command.EmailCode, command.BearerToken, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<ConfirmTwoFactorThroughApiCommandOutput>(
                result, HeimdallAuthMessages.TwoFactorRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            Enabled = result.Data!.Enabled ?? true,
            RecoveryCodes = result.Data.RecoveryCodes
        }).WithMessage(HeimdallAuthMessages.TwoFactorEnabled);
    }
}

public sealed class DisableTwoFactorThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<DisableTwoFactorThroughApiCommand, DisableTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<DisableTwoFactorThroughApiCommandOutput?>> HandleAsync(
        DisableTwoFactorThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.DisableTwoFactorAsync(
            command.Password, command.Code, command.RecoveryCode, command.BearerToken,
            cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<DisableTwoFactorThroughApiCommandOutput>(
                result, HeimdallAuthMessages.TwoFactorRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<DisableTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            Disabled = result.Data!.Disabled
        }).WithMessage(HeimdallAuthMessages.TwoFactorDisabled);
    }
}

public sealed class RegenerateRecoveryCodesThroughApiCommandHandler(
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<RegenerateRecoveryCodesThroughApiCommand,
        RegenerateRecoveryCodesThroughApiCommandOutput>
{
    public async Task<DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>> HandleAsync(
        RegenerateRecoveryCodesThroughApiCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.RegenerateRecoveryCodesAsync(
            command.Code, command.RecoveryCode, command.BearerToken, cancellationToken);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<RegenerateRecoveryCodesThroughApiCommandOutput>(
                result, HeimdallAuthMessages.TwoFactorRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>.New.WithData(new()
        {
            RecoveryCodes = result.Data!.RecoveryCodes
        }).WithMessage(HeimdallAuthMessages.RecoveryCodesRegenerated);
    }
}
