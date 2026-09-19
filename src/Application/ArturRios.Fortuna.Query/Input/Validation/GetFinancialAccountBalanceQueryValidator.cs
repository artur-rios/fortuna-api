using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetFinancialAccountBalanceQueryValidator
    : AbstractValidator<GetFinancialAccountBalanceQuery>
{
    public GetFinancialAccountBalanceQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(FinancialAccountMessages.NotFound);
        RuleFor(query => query.AsOf)
            .OptionalAsOfDate()
            .WithMessage(FinancialAccountMessages.AsOfOutOfRange);
    }
}
