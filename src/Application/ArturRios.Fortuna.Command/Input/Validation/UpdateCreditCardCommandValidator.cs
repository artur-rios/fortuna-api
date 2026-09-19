using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateCreditCardCommandValidator : AbstractValidator<UpdateCreditCardCommand>
{
    public UpdateCreditCardCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(CreditCardMessages.NameRequired)
            .TrimmedMaximumLength(200)
            .WithMessage(CreditCardMessages.NameTooLong);

        RuleFor(command => command.Issuer)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(CreditCardMessages.IssuerRequired)
            .TrimmedMaximumLength(200)
            .WithMessage(CreditCardMessages.IssuerTooLong);

        RuleFor(command => command.CreditLimit)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0)
            .WithMessage(CreditCardMessages.CreditLimitPositive)
            .Money()
            .WithMessage(CreditCardMessages.CreditLimitPrecisionInvalid);

        RuleFor(command => command.ClosingDay)
            .InclusiveBetween((short)1, (short)31)
            .WithMessage(CreditCardMessages.ClosingDayInvalid);

        RuleFor(command => command.DueDay)
            .InclusiveBetween((short)1, (short)31)
            .WithMessage(CreditCardMessages.DueDayInvalid);

        RuleFor(command => command.CurrencyCode)
            .Null()
            .WithMessage(CreditCardMessages.CurrencyImmutable);
    }
}
