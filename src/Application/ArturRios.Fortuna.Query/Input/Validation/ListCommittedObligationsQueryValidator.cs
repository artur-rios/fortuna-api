using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListCommittedObligationsQueryValidator
    : AbstractValidator<ListCommittedObligationsQuery>
{
    public ListCommittedObligationsQueryValidator(CashFlowProjectionOptions options)
    {
        RuleFor(query => query.HorizonDays)
            .GreaterThan(0)
            .WithMessage(CommittedObligationMessages.HorizonRequired)
            .LessThanOrEqualTo(options.MaximumHorizonDays)
            .WithMessage(CommittedObligationMessages.HorizonMaximum(
                options.MaximumHorizonDays));
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(CommittedObligationMessages.DisplayCurrencyInvalid);
    }
}
