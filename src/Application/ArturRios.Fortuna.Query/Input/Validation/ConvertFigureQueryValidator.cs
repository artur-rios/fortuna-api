using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ConvertFigureQueryValidator : AbstractValidator<ConvertFigureQuery>
{
    public const int MaximumAmounts = 100;

    public ConvertFigureQueryValidator()
    {
        RuleFor(query => query.DisplayCurrencyCode)
            .OptionalCurrencyCode()
            .WithMessage(FigureConversionMessages.DisplayCurrencyInvalid);
        RuleFor(query => query.FigureDate)
            .NotEmpty()
            .WithMessage(FigureConversionMessages.FigureDateRequired);
        RuleFor(query => query.Amounts)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(FigureConversionMessages.AmountsRequired)
            .Must(amounts => amounts!.Count <= MaximumAmounts)
            .WithMessage(FigureConversionMessages.TooManyAmounts);
        RuleForEach(query => query.Amounts)
            .NotNull()
            .WithMessage(FigureConversionMessages.AmountRequired)
            .ChildRules(amount =>
            {
                amount.RuleFor(item => item.CurrencyCode)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty()
                    .WithMessage(FigureConversionMessages.AmountCurrencyRequired)
                    .TrimmedCurrencyCode()
                    .WithMessage(FigureConversionMessages.AmountCurrencyInvalid);
                amount.RuleFor(item => item.Amount)
                    .PrecisionScale(19, 4, false)
                    .WithMessage(FigureConversionMessages.AmountPrecisionInvalid);
            });
    }
}
