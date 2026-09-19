using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetFinancialAccountByIdQueryValidator : AbstractValidator<GetFinancialAccountByIdQuery>
{
    public GetFinancialAccountByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(FinancialAccountMessages.NotFound);
    }
}
