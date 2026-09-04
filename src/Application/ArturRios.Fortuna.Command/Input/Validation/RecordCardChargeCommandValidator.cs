using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordCardChargeCommandValidator : AbstractValidator<RecordCardChargeCommand>
{
    public RecordCardChargeCommandValidator()
    {
        RuleFor(command => command.CreditCardId)
            .NotEmpty()
            .WithMessage(TransactionMessages.CreditCardIdRequired);
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(TransactionMessages.AmountPositive);
        RuleFor(command => command.Amount)
            .Must(amount => decimal.GetBits(amount)[3] >> 16 <= 4 &&
                Math.Abs(amount) < 1_000_000_000_000_000m)
            .When(command => command.Amount > 0m)
            .WithMessage(TransactionMessages.AmountPrecisionInvalid);
        RuleFor(command => command.OccurredOn)
            .NotEqual(default(DateOnly))
            .WithMessage(TransactionMessages.OccurredOnRequired);
    }
}
