using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetCreditCardStatementByIdQueryValidator : AbstractValidator<GetCreditCardStatementByIdQuery>
{
    public GetCreditCardStatementByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(CreditCardStatementMessages.NotFound);
    }
}
