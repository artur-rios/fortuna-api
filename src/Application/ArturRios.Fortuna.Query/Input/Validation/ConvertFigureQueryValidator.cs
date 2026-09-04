using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ConvertFigureQueryValidator : AbstractValidator<ConvertFigureQuery>
{
    public ConvertFigureQueryValidator()
    {
        RuleFor(query => query.DisplayCurrencyCode)
            .Length(3)
            .When(query => !string.IsNullOrWhiteSpace(query.DisplayCurrencyCode))
            .WithMessage(FigureConversionMessages.DisplayCurrencyInvalid);

        RuleFor(query => query.FigureDate)
            .NotEmpty()
            .WithMessage(FigureConversionMessages.FigureDateRequired);

        RuleFor(query => query.Amounts)
            .NotNull()
            .WithMessage(FigureConversionMessages.AmountsRequired);

        RuleForEach(query => query.Amounts)
            .ChildRules(amount =>
            {
                amount.RuleFor(item => item.CurrencyCode)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .WithMessage(FigureConversionMessages.AmountCurrencyRequired)
                    .Length(3)
                    .WithMessage(FigureConversionMessages.AmountCurrencyInvalid);
                amount.RuleFor(item => item.Amount)
                    .PrecisionScale(19, 4, false)
                    .WithMessage(FigureConversionMessages.AmountPrecisionInvalid);
            });
    }
}
