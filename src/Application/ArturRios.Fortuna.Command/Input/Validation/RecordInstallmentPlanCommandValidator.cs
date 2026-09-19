using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordInstallmentPlanCommandValidator
    : AbstractValidator<RecordInstallmentPlanCommand>
{
    public RecordInstallmentPlanCommandValidator(TimeProvider timeProvider)
    {
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

        RuleFor(command => command.CreditCardId)
            .NotEmpty().WithMessage(InstallmentPlanMessages.CreditCardIdRequired);
        RuleFor(command => command.CategoryId)
            .NotEmpty().WithMessage(InstallmentPlanMessages.CategoryIdRequired);
        RuleFor(command => command.TotalAmount)
            .GreaterThan(0m).WithMessage(InstallmentPlanMessages.TotalAmountPositive);
        RuleFor(command => command.TotalAmount)
            .Money()
            .When(command => command.TotalAmount > 0m)
            .WithMessage(InstallmentPlanMessages.TotalAmountPrecisionInvalid);
        RuleFor(command => command.InstallmentCount)
            .GreaterThanOrEqualTo((short)2)
            .WithMessage(InstallmentPlanMessages.InstallmentCountMinimum);
        RuleFor(command => command.PurchasedOn)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(InstallmentPlanMessages.PurchasedOnRequired)
            .LessThanOrEqualTo(maximumDate)
            .WithMessage(InstallmentPlanMessages.PurchasedOnTooFarInFuture);
        RuleFor(command => command.CurrencyCode)
            .OptionalCurrencyCode()
            .WithMessage(InstallmentPlanMessages.CurrencyCodeInvalid);
        RuleFor(command => command.Counterparty)
            .TrimmedMaximumLength(200)
            .WithMessage(InstallmentPlanMessages.CounterpartyTooLong);
        RuleFor(command => command.OwnerId)
            .Null().WithMessage(InstallmentPlanMessages.OwnerImmutable);
    }
}
