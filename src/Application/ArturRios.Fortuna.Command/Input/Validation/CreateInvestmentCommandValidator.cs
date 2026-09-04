using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateInvestmentCommandValidator : AbstractValidator<CreateInvestmentCommand>
{
    public CreateInvestmentCommandValidator()
    {
        RuleFor(command => command.Instrument)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(InvestmentMessages.InstrumentRequired)
            .MaximumLength(200)
            .WithMessage(InvestmentMessages.InstrumentTooLong);

        RuleFor(command => command.Institution)
            .MaximumLength(200)
            .WithMessage(InvestmentMessages.InstitutionTooLong);

        RuleFor(command => command.InvestmentType)
            .IsInEnum()
            .WithMessage(InvestmentMessages.InvestmentTypeInvalid);

        RuleFor(command => command.CurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(InvestmentMessages.CurrencyRequired)
            .Length(3)
            .WithMessage(InvestmentMessages.CurrencyInvalid);
    }
}
