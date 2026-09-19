using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class SettleCreditCardStatementCommandValidator
    : AbstractValidator<SettleCreditCardStatementCommand>
{
    public SettleCreditCardStatementCommandValidator(TimeProvider timeProvider)
    {
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

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
            .Money()
            .When(command => command.Amount > 0m)
            .WithMessage(CreditCardStatementMessages.PaymentAmountPrecisionInvalid);
        RuleFor(command => command.PaymentDate)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(CreditCardStatementMessages.PaymentDateRequired)
            .LessThanOrEqualTo(maximumDate)
            .WithMessage(CreditCardStatementMessages.PaymentDateTooFarInFuture);
    }
}
