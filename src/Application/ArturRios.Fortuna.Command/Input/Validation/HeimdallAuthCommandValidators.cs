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
