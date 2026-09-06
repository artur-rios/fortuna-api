using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateCounterpartyCommandValidator : AbstractValidator<CreateCounterpartyCommand>
{
    public CreateCounterpartyCommandValidator() => RuleFor(command => command.Name)
        .Cascade(CascadeMode.Stop)
        .NotEmpty()
        .WithMessage(CounterpartyMessages.NameRequired)
        .MaximumLength(200)
        .WithMessage(CounterpartyMessages.NameTooLong);
}

public sealed class UpdateCounterpartyCommandValidator : AbstractValidator<UpdateCounterpartyCommand>
{
    public UpdateCounterpartyCommandValidator() => RuleFor(command => command.Name)
        .Cascade(CascadeMode.Stop)
        .NotEmpty()
        .WithMessage(CounterpartyMessages.NameRequired)
        .MaximumLength(200)
        .WithMessage(CounterpartyMessages.NameTooLong);
}

public sealed class MergeCounterpartiesCommandValidator : AbstractValidator<MergeCounterpartiesCommand>
{
    public MergeCounterpartiesCommandValidator() => RuleFor(command => command.TargetId)
        .NotEmpty()
        .WithMessage(CounterpartyMessages.TargetIdInvalid);
}
