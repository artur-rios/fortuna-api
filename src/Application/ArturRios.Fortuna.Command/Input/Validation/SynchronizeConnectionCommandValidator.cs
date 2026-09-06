using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class SynchronizeConnectionCommandValidator
    : AbstractValidator<SynchronizeConnectionCommand>
{
    public SynchronizeConnectionCommandValidator()
    {
        RuleFor(command => command)
            .Must(command => !command.PeriodStart.HasValue ||
                !command.PeriodEnd.HasValue ||
                command.PeriodStart <= command.PeriodEnd)
            .WithMessage(PluggySynchronizationMessages.PeriodInvalid);
    }
}
