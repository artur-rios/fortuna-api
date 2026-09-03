using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateLocalAccountCommandValidator : AbstractValidator<CreateLocalAccountCommand>
{
    public CreateLocalAccountCommandValidator()
    {
        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .WithMessage(LocalAccountMessages.NameRequired)
            .MaximumLength(200)
            .WithMessage(LocalAccountMessages.NameTooLong);

        RuleFor(command => command.Secret)
            .NotEmpty()
            .WithMessage(LocalAccountMessages.SecretRequired)
            .MinimumLength(8)
            .WithMessage(LocalAccountMessages.SecretTooShort);

        RuleFor(command => command.StorageMode)
            .IsInEnum()
            .WithMessage(LocalAccountMessages.StorageModeInvalid);
    }
}
