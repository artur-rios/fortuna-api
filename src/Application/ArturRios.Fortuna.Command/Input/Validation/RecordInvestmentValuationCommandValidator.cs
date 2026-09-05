using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordInvestmentValuationCommandValidator
    : AbstractValidator<RecordInvestmentValuationCommand>
{
    public RecordInvestmentValuationCommandValidator(TimeProvider timeProvider)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(InvestmentMessages.InvestmentIdRequired);
        RuleFor(command => command.Value)
            .PrecisionScale(19, 4, false)
            .WithMessage(InvestmentMessages.ValuationValuePrecisionInvalid);
        RuleFor(command => command.ValuedOn)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(InvestmentMessages.ValuedOnRequired)
            .LessThanOrEqualTo(today)
            .WithMessage(InvestmentMessages.ValuedOnFuture);
    }
}
