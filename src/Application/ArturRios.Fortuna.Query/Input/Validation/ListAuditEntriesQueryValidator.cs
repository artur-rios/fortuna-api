using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListAuditEntriesQueryValidator : AbstractValidator<ListAuditEntriesQuery>
{
    public ListAuditEntriesQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(AuditEntryMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage(AuditEntryMessages.InvalidPageSize);

        RuleFor(query => query.EntityType)
            .MaximumLength(100)
            .WithMessage(AuditEntryMessages.EntityTypeTooLong);

        RuleFor(query => query.Operation)
            .MaximumLength(150)
            .WithMessage(AuditEntryMessages.OperationTooLong);

        RuleFor(query => query.Outcome)
            .IsInEnum()
            .When(query => query.Outcome.HasValue)
            .WithMessage(AuditEntryMessages.OutcomeInvalid);

        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .When(query => query.From.HasValue && query.To.HasValue)
            .WithMessage(AuditEntryMessages.PeriodInvalid);
    }
}
