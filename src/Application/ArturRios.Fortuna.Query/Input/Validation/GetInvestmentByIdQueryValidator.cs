using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetInvestmentByIdQueryValidator : AbstractValidator<GetInvestmentByIdQuery>
{
    public GetInvestmentByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(InvestmentMessages.InvestmentIdRequired);
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(InvestmentQueryValidation.IsOptionalCurrencyCode)
            .WithMessage(InvestmentMessages.DisplayCurrencyInvalid);
    }
}
