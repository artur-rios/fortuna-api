using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateFinancialAccountCommandValidator
    : AbstractValidator<CreateFinancialAccountCommand>
{
    public CreateFinancialAccountCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(FinancialAccountMessages.NameRequired)
            .MaximumLength(200)
            .WithMessage(FinancialAccountMessages.NameTooLong);

        RuleFor(command => command.Institution)
            .MaximumLength(200)
            .WithMessage(FinancialAccountMessages.InstitutionTooLong);

        RuleFor(command => command.AccountType)
            .IsInEnum()
            .WithMessage(FinancialAccountMessages.AccountTypeInvalid);

        RuleFor(command => command.CurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(FinancialAccountMessages.CurrencyRequired)
            .Length(3)
            .WithMessage(FinancialAccountMessages.CurrencyInvalid);

        RuleFor(command => command.OpeningBalance)
            .PrecisionScale(19, 4, false)
            .WithMessage(FinancialAccountMessages.OpeningBalancePrecisionInvalid);
    }
}
