using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordInvestmentMovementCommandValidator
    : AbstractValidator<RecordInvestmentMovementCommand>
{
    public RecordInvestmentMovementCommandValidator(TimeProvider timeProvider)
    {
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(InvestmentMessages.InvestmentIdRequired);
        RuleFor(command => command.MovementType)
            .IsInEnum()
            .WithMessage(InvestmentMessages.MovementTypeInvalid);
        RuleFor(command => command.Amount)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0m)
            .WithMessage(InvestmentMessages.MovementAmountPositive)
            .PrecisionScale(19, 4, false)
            .WithMessage(InvestmentMessages.MovementAmountPrecisionInvalid);
        RuleFor(command => command.OccurredOn)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(InvestmentMessages.OccurredOnRequired)
            .LessThanOrEqualTo(maximumDate)
            .WithMessage(InvestmentMessages.OccurredOnTooFarInFuture);
        RuleFor(command => command.FinancialAccountId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage(InvestmentMessages.FinancialAccountIdInvalid);
        RuleFor(command => command.FinancialAccountId)
            .Must(_ => false)
            .When(command => command.FinancialAccountId.HasValue &&
                command.MovementType != InvestmentMovementType.Contribution)
            .WithMessage(InvestmentMessages.FundingRequiresContribution);
    }
}
