using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class MaterializeRecurringTransactionsCommandValidator
    : AbstractValidator<MaterializeRecurringTransactionsCommand>
{
    public MaterializeRecurringTransactionsCommandValidator()
    {
        RuleFor(command => command.OwnerId).Null()
            .WithMessage(RecurringTransactionMessages.OwnerImmutable);
    }
}
