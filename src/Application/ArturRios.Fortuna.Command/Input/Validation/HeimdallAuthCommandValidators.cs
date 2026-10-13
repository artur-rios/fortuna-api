using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class LoginThroughApiCommandValidator : AbstractValidator<LoginThroughApiCommand>
{
    public LoginThroughApiCommandValidator()
    {
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(HeimdallAuthMessages.EmailRequired)
            .EmailAddress().WithMessage(HeimdallAuthMessages.EmailInvalid);
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage(HeimdallAuthMessages.PasswordRequired);
    }
}

public sealed class GoogleSignInThroughApiCommandValidator : AbstractValidator<GoogleSignInThroughApiCommand>
{
    public GoogleSignInThroughApiCommandValidator() =>
        RuleFor(command => command.IdToken)
            .NotEmpty().WithMessage(HeimdallAuthMessages.GoogleIdTokenRequired);
}

public sealed class VerifyTwoFactorThroughApiCommandValidator : AbstractValidator<VerifyTwoFactorThroughApiCommand>
{
    public VerifyTwoFactorThroughApiCommandValidator()
    {
        RuleFor(command => command.ChallengeToken)
            .NotEmpty().WithMessage(HeimdallAuthMessages.ChallengeTokenRequired);
        RuleFor(command => command)
            .Must(command => !string.IsNullOrWhiteSpace(command.Code) ||
                !string.IsNullOrWhiteSpace(command.RecoveryCode))
            .WithMessage(HeimdallAuthMessages.FactorRequired);
        RuleFor(command => command)
            .Must(command => string.IsNullOrWhiteSpace(command.Code) ||
                string.IsNullOrWhiteSpace(command.RecoveryCode))
            .WithMessage(HeimdallAuthMessages.FactorAmbiguous);
    }
}

public sealed class ResendTwoFactorChallengeCodeThroughApiCommandValidator
    : AbstractValidator<ResendTwoFactorChallengeCodeThroughApiCommand>
{
    public ResendTwoFactorChallengeCodeThroughApiCommandValidator() =>
        RuleFor(command => command.ChallengeToken)
            .NotEmpty().WithMessage(HeimdallAuthMessages.ChallengeTokenRequired);
}

public sealed class RequestPasswordRecoveryThroughApiCommandValidator
    : AbstractValidator<RequestPasswordRecoveryThroughApiCommand>
{
    public RequestPasswordRecoveryThroughApiCommandValidator() =>
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(HeimdallAuthMessages.EmailRequired)
            .EmailAddress().WithMessage(HeimdallAuthMessages.EmailInvalid);
}

/// <summary>
///     Heimdall's UC-47 field rules, applied here so a malformed request costs no round trip. Heimdall
///     stays the authority and validates again.
/// </summary>
public sealed class RegisterThroughApiCommandValidator : AbstractValidator<RegisterThroughApiCommand>
{
    public RegisterThroughApiCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage(HeimdallAuthMessages.NameRequired)
            .Must(name => name.Trim().Length <= 200).WithMessage(HeimdallAuthMessages.NameTooLong);
        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(HeimdallAuthMessages.EmailRequired)
            .EmailAddress().WithMessage(HeimdallAuthMessages.EmailInvalid);
        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(HeimdallAuthMessages.PasswordRequired)
            .MinimumLength(8).WithMessage(HeimdallAuthMessages.PasswordTooShort);
    }
}

public sealed class ResetPasswordThroughApiCommandValidator
    : AbstractValidator<ResetPasswordThroughApiCommand>
{
    public ResetPasswordThroughApiCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage(HeimdallAuthMessages.TokenRequired);
        RuleFor(command => command.NewPassword)
            .NotEmpty().WithMessage(HeimdallAuthMessages.NewPasswordRequired);
    }
}

public sealed class VerifyEmailThroughApiCommandValidator
    : AbstractValidator<VerifyEmailThroughApiCommand>
{
    public VerifyEmailThroughApiCommandValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage(HeimdallAuthMessages.TokenRequired);
}

public sealed class EnableTwoFactorThroughApiCommandValidator
    : AbstractValidator<EnableTwoFactorThroughApiCommand>
{
    private static readonly string[] AllowedMethods = ["App", "Email"];

    public EnableTwoFactorThroughApiCommandValidator()
    {
        RuleFor(command => command.Methods)
            .NotEmpty().WithMessage(HeimdallAuthMessages.MethodsRequired);
        RuleForEach(command => command.Methods)
            .Must(method => AllowedMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
            .WithMessage(HeimdallAuthMessages.MethodInvalid);
    }
}

public sealed class ConfirmTwoFactorThroughApiCommandValidator
    : AbstractValidator<ConfirmTwoFactorThroughApiCommand>
{
    public ConfirmTwoFactorThroughApiCommandValidator() =>
        RuleFor(command => command)
            .Must(command => !string.IsNullOrWhiteSpace(command.AppCode) ||
                !string.IsNullOrWhiteSpace(command.EmailCode))
            .WithMessage(HeimdallAuthMessages.ConfirmationCodeRequired);
}

public sealed class DisableTwoFactorThroughApiCommandValidator
    : AbstractValidator<DisableTwoFactorThroughApiCommand>
{
    public DisableTwoFactorThroughApiCommandValidator()
    {
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage(HeimdallAuthMessages.PasswordRequired);
        FactorRules.Add(this);
    }
}

public sealed class RegenerateRecoveryCodesThroughApiCommandValidator
    : AbstractValidator<RegenerateRecoveryCodesThroughApiCommand>
{
    public RegenerateRecoveryCodesThroughApiCommandValidator() => FactorRules.Add(this);
}

internal static class FactorRules
{
    internal static void Add<T>(AbstractValidator<T> validator)
        where T : AuthenticatedHeimdallCommand
    {
        validator.RuleFor(command => command)
            .Must(HasFactor)
            .WithMessage(HeimdallAuthMessages.FactorRequired);
        validator.RuleFor(command => command)
            .Must(HasSingleFactor)
            .WithMessage(HeimdallAuthMessages.FactorAmbiguous);
    }

    private static bool HasFactor<T>(T command) where T : AuthenticatedHeimdallCommand =>
        command switch
        {
            DisableTwoFactorThroughApiCommand value =>
                !string.IsNullOrWhiteSpace(value.Code) || !string.IsNullOrWhiteSpace(value.RecoveryCode),
            RegenerateRecoveryCodesThroughApiCommand value =>
                !string.IsNullOrWhiteSpace(value.Code) || !string.IsNullOrWhiteSpace(value.RecoveryCode),
            _ => false
        };

    private static bool HasSingleFactor<T>(T command) where T : AuthenticatedHeimdallCommand =>
        command switch
        {
            DisableTwoFactorThroughApiCommand value =>
                string.IsNullOrWhiteSpace(value.Code) || string.IsNullOrWhiteSpace(value.RecoveryCode),
            RegenerateRecoveryCodesThroughApiCommand value =>
                string.IsNullOrWhiteSpace(value.Code) || string.IsNullOrWhiteSpace(value.RecoveryCode),
            _ => false
        };
}
