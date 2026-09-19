using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class AuthenticateLocalAccountCommandValidator
    : AbstractValidator<AuthenticateLocalAccountCommand>
{
    public AuthenticateLocalAccountCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(LocalAuthenticationMessages.NameRequired)
            .Must(name => name.Trim().Length <= LocalAccountInputLimits.NameMaximumLength)
            .WithMessage(LocalAuthenticationMessages.NameTooLong);

        // No minimum length here: a short secret is simply wrong, and saying so separately would
        // tell a caller more than "invalid credentials" does.
        RuleFor(command => command.Secret)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(LocalAuthenticationMessages.SecretRequired)
            .MaximumLength(LocalAccountInputLimits.SecretMaximumLength)
            .WithMessage(LocalAuthenticationMessages.SecretTooLong);
    }
}
