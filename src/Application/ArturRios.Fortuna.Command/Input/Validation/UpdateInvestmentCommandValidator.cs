using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateInvestmentCommandValidator : AbstractValidator<UpdateInvestmentCommand>
{
    public UpdateInvestmentCommandValidator()
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
            .Null()
            .WithMessage(InvestmentMessages.CurrencyImmutable);
    }
}
