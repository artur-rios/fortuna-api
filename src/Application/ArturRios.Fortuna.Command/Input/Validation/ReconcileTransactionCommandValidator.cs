using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class ReconcileTransactionCommandValidator
    : AbstractValidator<ReconcileTransactionCommand>
{
    public ReconcileTransactionCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(TransactionMessages.TransactionIdRequired);
        RuleFor(command => command.ImportJobId)
            .Must(id => id.HasValue && id.Value != Guid.Empty)
            .When(command => !command.Unreconcile)
            .WithMessage(TransactionMessages.ImportJobIdRequired);
        RuleFor(command => command.ImportedRecordId)
            .Must(id => id.HasValue && id.Value > 0)
            .When(command => !command.Unreconcile)
            .WithMessage(TransactionMessages.ImportedRecordIdRequired);
        RuleFor(command => command.ImportJobId)
            .Null()
            .When(command => command.Unreconcile)
            .WithMessage(TransactionMessages.UnreconcileReferencesForbidden);
        RuleFor(command => command.ImportedRecordId)
            .Null()
            .When(command => command.Unreconcile)
            .WithMessage(TransactionMessages.UnreconcileReferencesForbidden);
    }
}
