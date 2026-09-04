using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordManualExchangeRateCommandValidator
    : AbstractValidator<RecordManualExchangeRateCommand>
{
    public RecordManualExchangeRateCommandValidator()
    {
        RuleFor(command => command.BaseCurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ManualExchangeRateMessages.BaseCurrencyRequired)
            .Length(3)
            .WithMessage(ManualExchangeRateMessages.BaseCurrencyInvalid);

        RuleFor(command => command.QuoteCurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ManualExchangeRateMessages.QuoteCurrencyRequired)
            .Length(3)
            .WithMessage(ManualExchangeRateMessages.QuoteCurrencyInvalid);

        RuleFor(command => command.Rate)
            .GreaterThan(0)
            .WithMessage(ManualExchangeRateMessages.RateMustBePositive)
            .PrecisionScale(19, 8, false)
            .WithMessage(ManualExchangeRateMessages.RatePrecisionInvalid);

        RuleFor(command => command.RateDate)
            .NotEmpty()
            .WithMessage(ManualExchangeRateMessages.RateDateRequired);

        RuleFor(command => command)
            .Must(command => !string.Equals(
                command.BaseCurrencyCode?.Trim(),
                command.QuoteCurrencyCode?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .WithMessage(ManualExchangeRateMessages.CurrenciesMustDiffer);
    }
}
