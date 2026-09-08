using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetNetPositionQueryValidator : AbstractValidator<GetNetPositionQuery>
{
    public GetNetPositionQueryValidator()
    {
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(NetPositionMessages.DisplayCurrencyInvalid);
    }
}
