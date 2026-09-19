using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetCreditCardByIdQueryValidator : AbstractValidator<GetCreditCardByIdQuery>
{
    public GetCreditCardByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(CreditCardMessages.NotFound);
    }
}
