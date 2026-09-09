using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed record HeimdallAuthOptions(Guid ScopeId);

public sealed class LoginThroughApiCommandHandler(
    IValidator<LoginThroughApiCommand> validator,
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<LoginThroughApiCommand, LoginThroughApiCommandOutput>
{
    public async Task<DataOutput<LoginThroughApiCommandOutput?>> HandleAsync(LoginThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<LoginThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.LoginAsync(
            command.Email.Trim(), command.Password, options.ScopeId, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return Failure<LoginThroughApiCommandOutput>(result.Outcome,
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

    internal static DataOutput<T?> Failure<T>(
        HeimdallAuthOutcome outcome, string rejected, string? notFound = null)
        where T : class => outcome switch
        {
            HeimdallAuthOutcome.InvalidRequest => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Rejected => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Unavailable => DataOutput<T?>.New.WithError(
                HeimdallAuthMessages.ServiceUnavailable),
            HeimdallAuthOutcome.NotFound => DataOutput<T?>.New.WithError(notFound ?? rejected),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
}

public sealed class GoogleSignInThroughApiCommandHandler(
    IValidator<GoogleSignInThroughApiCommand> validator,
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<GoogleSignInThroughApiCommand, GoogleSignInThroughApiCommandOutput>
{
    public async Task<DataOutput<GoogleSignInThroughApiCommandOutput?>> HandleAsync(
        GoogleSignInThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<GoogleSignInThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.GoogleSignInAsync(
            command.IdToken, options.ScopeId, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<GoogleSignInThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.AuthenticationRejected);
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
    IValidator<VerifyTwoFactorThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<VerifyTwoFactorThroughApiCommand, VerifyTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<VerifyTwoFactorThroughApiCommandOutput?>> HandleAsync(
        VerifyTwoFactorThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<VerifyTwoFactorThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.VerifyTwoFactorAsync(
            command.ChallengeToken, command.Code, command.RecoveryCode, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<VerifyTwoFactorThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.TwoFactorRejected);
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
        GoogleSignOutThroughApiCommand command)
    {
        var result = await gateway.GoogleSignOutAsync(command.BearerToken, CancellationToken.None);
        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<GoogleSignOutThroughApiCommandOutput?>.New
                .WithData(new GoogleSignOutThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.SignedOutSuccessfully)
            : LoginThroughApiCommandHandler.Failure<GoogleSignOutThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.AuthenticationRejected);
    }
}

public sealed class RequestPasswordRecoveryThroughApiCommandHandler(
    IValidator<RequestPasswordRecoveryThroughApiCommand> validator,
    IHeimdallAuthGateway gateway,
    HeimdallAuthOptions options)
    : ICommandHandlerAsync<RequestPasswordRecoveryThroughApiCommand,
        RequestPasswordRecoveryThroughApiCommandOutput>
{
    public async Task<DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>> HandleAsync(
        RequestPasswordRecoveryThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.RequestPasswordRecoveryAsync(
            command.Email.Trim(), options.ScopeId, CancellationToken.None);
        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<RequestPasswordRecoveryThroughApiCommandOutput?>.New
                .WithData(new RequestPasswordRecoveryThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.PasswordRecoveryRequested)
            : LoginThroughApiCommandHandler.Failure<RequestPasswordRecoveryThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class ResetPasswordThroughApiCommandHandler(
    IValidator<ResetPasswordThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ResetPasswordThroughApiCommand, ResetPasswordThroughApiCommandOutput>
{
    public async Task<DataOutput<ResetPasswordThroughApiCommandOutput?>> HandleAsync(
        ResetPasswordThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<ResetPasswordThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.ResetPasswordAsync(
            command.Token, command.NewPassword, CancellationToken.None);
        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<ResetPasswordThroughApiCommandOutput?>.New
                .WithData(new ResetPasswordThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.PasswordReset)
            : LoginThroughApiCommandHandler.Failure<ResetPasswordThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class VerifyEmailThroughApiCommandHandler(
    IValidator<VerifyEmailThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<VerifyEmailThroughApiCommand, VerifyEmailThroughApiCommandOutput>
{
    public async Task<DataOutput<VerifyEmailThroughApiCommandOutput?>> HandleAsync(
        VerifyEmailThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<VerifyEmailThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.VerifyEmailAsync(command.Token, CancellationToken.None);
        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<VerifyEmailThroughApiCommandOutput?>.New
                .WithData(new VerifyEmailThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.EmailVerified)
            : LoginThroughApiCommandHandler.Failure<VerifyEmailThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class ResendVerificationThroughApiCommandHandler(IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ResendVerificationThroughApiCommand,
        ResendVerificationThroughApiCommandOutput>
{
    public async Task<DataOutput<ResendVerificationThroughApiCommandOutput?>> HandleAsync(
        ResendVerificationThroughApiCommand command)
    {
        var result = await gateway.ResendVerificationAsync(
            command.BearerToken, CancellationToken.None);
        return result.Outcome == HeimdallAuthOutcome.Succeeded
            ? DataOutput<ResendVerificationThroughApiCommandOutput?>.New
                .WithData(new ResendVerificationThroughApiCommandOutput())
                .WithMessage(HeimdallAuthMessages.VerificationResent)
            : LoginThroughApiCommandHandler.Failure<ResendVerificationThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected);
    }
}

public sealed class GetTwoFactorStatusThroughApiCommandHandler(IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<GetTwoFactorStatusThroughApiCommand,
        GetTwoFactorStatusThroughApiCommandOutput>
{
    public async Task<DataOutput<GetTwoFactorStatusThroughApiCommandOutput?>> HandleAsync(
        GetTwoFactorStatusThroughApiCommand command)
    {
        var result = await gateway.GetTwoFactorStatusAsync(command.BearerToken, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<GetTwoFactorStatusThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected,
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
    IValidator<EnableTwoFactorThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<EnableTwoFactorThroughApiCommand, EnableTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<EnableTwoFactorThroughApiCommandOutput?>> HandleAsync(
        EnableTwoFactorThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<EnableTwoFactorThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.EnableTwoFactorAsync(
            command.Methods, command.BearerToken, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<EnableTwoFactorThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.RequestRejected);
        }

        return DataOutput<EnableTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            OtpAuthUri = result.Data!.OtpAuthUri,
            EmailCodeSent = result.Data.EmailCodeSent
        }).WithMessage(HeimdallAuthMessages.TwoFactorSetupStarted);
    }
}

public sealed class ConfirmTwoFactorThroughApiCommandHandler(
    IValidator<ConfirmTwoFactorThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<ConfirmTwoFactorThroughApiCommand, ConfirmTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>> HandleAsync(
        ConfirmTwoFactorThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<ConfirmTwoFactorThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.ConfirmTwoFactorAsync(
            command.AppCode, command.EmailCode, command.BearerToken, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<ConfirmTwoFactorThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.TwoFactorRejected,
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
    IValidator<DisableTwoFactorThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<DisableTwoFactorThroughApiCommand, DisableTwoFactorThroughApiCommandOutput>
{
    public async Task<DataOutput<DisableTwoFactorThroughApiCommandOutput?>> HandleAsync(
        DisableTwoFactorThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<DisableTwoFactorThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.DisableTwoFactorAsync(
            command.Password, command.Code, command.RecoveryCode, command.BearerToken,
            CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<DisableTwoFactorThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.TwoFactorRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<DisableTwoFactorThroughApiCommandOutput?>.New.WithData(new()
        {
            Disabled = result.Data!.Disabled
        }).WithMessage(HeimdallAuthMessages.TwoFactorDisabled);
    }
}

public sealed class RegenerateRecoveryCodesThroughApiCommandHandler(
    IValidator<RegenerateRecoveryCodesThroughApiCommand> validator,
    IHeimdallAuthGateway gateway)
    : ICommandHandlerAsync<RegenerateRecoveryCodesThroughApiCommand,
        RegenerateRecoveryCodesThroughApiCommandOutput>
{
    public async Task<DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>> HandleAsync(
        RegenerateRecoveryCodesThroughApiCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var result = await gateway.RegenerateRecoveryCodesAsync(
            command.Code, command.RecoveryCode, command.BearerToken, CancellationToken.None);
        if (result.Outcome != HeimdallAuthOutcome.Succeeded)
        {
            return LoginThroughApiCommandHandler.Failure<RegenerateRecoveryCodesThroughApiCommandOutput>(
                result.Outcome, HeimdallAuthMessages.TwoFactorRejected,
                HeimdallAuthMessages.TwoFactorNotFound);
        }

        return DataOutput<RegenerateRecoveryCodesThroughApiCommandOutput?>.New.WithData(new()
        {
            RecoveryCodes = result.Data!.RecoveryCodes
        }).WithMessage(HeimdallAuthMessages.RecoveryCodesRegenerated);
    }
}
