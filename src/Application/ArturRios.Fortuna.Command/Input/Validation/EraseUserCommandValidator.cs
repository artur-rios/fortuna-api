using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class EraseUserCommandValidator : AbstractValidator<EraseUserCommand>
{
    public EraseUserCommandValidator()
    {
        RuleFor(command => command.Confirmation)
            .Must(confirmation => string.Equals(confirmation, "ERASE", StringComparison.Ordinal))
            .WithMessage(UserErasureMessages.ConfirmationInvalid);
    }
}
