using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateLocalAccountCommandValidator : AbstractValidator<CreateLocalAccountCommand>
{
    public CreateLocalAccountCommandValidator()
    {
        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(LocalAccountMessages.NameRequired)
            .MaximumLength(LocalAccountInputLimits.NameMaximumLength)
            .WithMessage(LocalAccountMessages.NameTooLong);

        RuleFor(command => command.Secret)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(LocalAccountMessages.SecretRequired)
            .MinimumLength(LocalAccountInputLimits.SecretMinimumLength)
            .WithMessage(LocalAccountMessages.SecretTooShort)
            .MaximumLength(LocalAccountInputLimits.SecretMaximumLength)
            .WithMessage(LocalAccountMessages.SecretTooLong);

        RuleFor(command => command.StorageMode)
            .IsInEnum()
            .WithMessage(LocalAccountMessages.StorageModeInvalid);
    }
}
