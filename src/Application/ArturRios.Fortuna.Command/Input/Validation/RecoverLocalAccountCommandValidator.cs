using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecoverLocalAccountCommandValidator : AbstractValidator<RecoverLocalAccountCommand>
{
    public RecoverLocalAccountCommandValidator()
    {
        RuleFor(command => command.NewSecret)
            .NotEmpty()
            .WithMessage(LocalAccountRecoveryMessages.NewSecretRequired)
            .MinimumLength(8)
            .WithMessage(LocalAccountRecoveryMessages.NewSecretTooShort);
    }
}
