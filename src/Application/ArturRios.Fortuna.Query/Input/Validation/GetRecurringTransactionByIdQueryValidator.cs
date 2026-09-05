using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetRecurringTransactionByIdQueryValidator
    : AbstractValidator<GetRecurringTransactionByIdQuery>
{
    public GetRecurringTransactionByIdQueryValidator() => RuleFor(query => query.Id)
        .NotEmpty()
        .WithMessage(RecurringTransactionMessages.IdRequired);
}
