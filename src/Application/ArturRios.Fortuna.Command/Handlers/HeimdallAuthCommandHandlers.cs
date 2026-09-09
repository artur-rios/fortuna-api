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

    internal static DataOutput<T?> Failure<T>(HeimdallAuthOutcome outcome, string rejected)
        where T : class => outcome switch
        {
            HeimdallAuthOutcome.InvalidRequest => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Rejected => DataOutput<T?>.New.WithError(rejected),
            HeimdallAuthOutcome.Unavailable => DataOutput<T?>.New.WithError(
                HeimdallAuthMessages.ServiceUnavailable),
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
