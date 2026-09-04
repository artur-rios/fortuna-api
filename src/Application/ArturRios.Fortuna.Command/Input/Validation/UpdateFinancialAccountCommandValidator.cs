using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateFinancialAccountCommandValidator
    : AbstractValidator<UpdateFinancialAccountCommand>
{
    public UpdateFinancialAccountCommandValidator()
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

        RuleFor(command => command.OwnerId)
            .Null()
            .WithMessage(FinancialAccountMessages.OwnerImmutable);

        RuleFor(command => command.CurrencyCode)
            .Null()
            .WithMessage(FinancialAccountMessages.CurrencyImmutable);

        RuleFor(command => command.OpeningBalance)
            .Null()
            .WithMessage(FinancialAccountMessages.OpeningBalanceImmutable);
    }
}
