using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetCurrencyByCodeQueryValidator : AbstractValidator<GetCurrencyByCodeQuery>
{
    public GetCurrencyByCodeQueryValidator()
    {
        RuleFor(query => query.Code)
            .TrimmedCurrencyCode()
            .WithMessage(CurrencyMessages.CurrencyNotFound);
    }
}
