using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class ReassignCategoryTransactionsCommandValidator
    : AbstractValidator<ReassignCategoryTransactionsCommand>
{
    public ReassignCategoryTransactionsCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(CategoryMessages.NotFound);

        RuleFor(command => command.TargetCategoryId)
            .NotEmpty()
            .WithMessage(CategoryMessages.TargetCategoryIdInvalid);

        RuleFor(command => command.TargetCategoryId)
            .NotEqual(command => command.Id)
            .When(command => command.Id != Guid.Empty &&
                command.TargetCategoryId != Guid.Empty)
            .WithMessage(CategoryMessages.SourceAndTargetMustDiffer);
    }
}
