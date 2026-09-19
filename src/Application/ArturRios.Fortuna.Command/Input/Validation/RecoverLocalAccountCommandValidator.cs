using System.Text.RegularExpressions;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecoverLocalAccountCommandValidator : AbstractValidator<RecoverLocalAccountCommand>
{
    public RecoverLocalAccountCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(LocalAccountRecoveryMessages.NameRequired)
            .Must(name => name.Trim().Length <= LocalAccountInputLimits.NameMaximumLength)
            .WithMessage(LocalAccountRecoveryMessages.NameTooLong);

        RuleFor(command => command.RecoveryCode)
            .Cascade(CascadeMode.Stop)
            .Must(code => !string.IsNullOrWhiteSpace(code))
            .WithMessage(LocalAccountRecoveryMessages.RecoveryCodeRequired)
            .Must(code => Regex.IsMatch(code.Trim(), LocalAccountInputLimits.RecoveryCodePattern))
            .WithMessage(LocalAccountRecoveryMessages.RecoveryCodeFormatInvalid);

        RuleFor(command => command.NewSecret)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(LocalAccountRecoveryMessages.NewSecretRequired)
            .MinimumLength(LocalAccountInputLimits.SecretMinimumLength)
            .WithMessage(LocalAccountRecoveryMessages.NewSecretTooShort)
            .MaximumLength(LocalAccountInputLimits.SecretMaximumLength)
            .WithMessage(LocalAccountRecoveryMessages.NewSecretTooLong);
    }
}
