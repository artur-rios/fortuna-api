using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ProjectCashFlowQueryValidator : AbstractValidator<ProjectCashFlowQuery>
{
    public ProjectCashFlowQueryValidator(CashFlowProjectionOptions options)
    {
        RuleFor(query => query.HorizonDays)
            .GreaterThan(0)
            .WithMessage(CashFlowProjectionMessages.HorizonRequired)
            .LessThanOrEqualTo(options.MaximumHorizonDays)
            .WithMessage(CashFlowProjectionMessages.HorizonMaximum(options.MaximumHorizonDays));
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(CashFlowProjectionMessages.DisplayCurrencyInvalid);
        RuleFor(query => query.Periodicity).IsInEnum();
    }
}
