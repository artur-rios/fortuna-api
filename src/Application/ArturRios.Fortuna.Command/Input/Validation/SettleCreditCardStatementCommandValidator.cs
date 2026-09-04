using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class SettleCreditCardStatementCommandValidator
    : AbstractValidator<SettleCreditCardStatementCommand>
{
    public SettleCreditCardStatementCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(CreditCardStatementMessages.StatementIdRequired);
        RuleFor(command => command.FinancialAccountId)
            .NotEmpty()
            .WithMessage(CreditCardStatementMessages.FinancialAccountIdRequired);
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(CreditCardStatementMessages.PaymentAmountPositive);
        RuleFor(command => command.Amount)
            .Must(amount => decimal.GetBits(amount)[3] >> 16 <= 4 &&
                Math.Abs(amount) < 1_000_000_000_000_000m)
            .When(command => command.Amount > 0m)
            .WithMessage(CreditCardStatementMessages.PaymentAmountPrecisionInvalid);
        RuleFor(command => command.PaymentDate)
            .NotEqual(default(DateOnly))
            .WithMessage(CreditCardStatementMessages.PaymentDateRequired);
    }
}
