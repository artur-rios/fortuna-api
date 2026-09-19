using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetNetPositionQueryValidator : AbstractValidator<GetNetPositionQuery>
{
    public GetNetPositionQueryValidator()
    {
        RuleFor(query => query.DisplayCurrencyCode)
            .OptionalCurrencyCode()
            .WithMessage(NetPositionMessages.DisplayCurrencyInvalid);
        RuleFor(query => query.AsOf)
            .OptionalAsOfDate()
            .WithMessage(NetPositionMessages.AsOfOutOfRange);
    }
}
