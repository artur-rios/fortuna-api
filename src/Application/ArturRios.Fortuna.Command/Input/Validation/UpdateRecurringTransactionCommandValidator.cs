using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateRecurringTransactionCommandValidator
    : AbstractValidator<UpdateRecurringTransactionCommand>
{
    public UpdateRecurringTransactionCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty()
            .WithMessage(RecurringTransactionMessages.IdRequired);
        RuleFor(command => command).Must(command =>
            (command.FinancialAccountId.HasValue && command.FinancialAccountId != Guid.Empty) ^
            (command.CreditCardId.HasValue && command.CreditCardId != Guid.Empty))
            .WithMessage(RecurringTransactionMessages.ExactlyOneTargetRequired);
        RuleFor(command => command.CategoryId).NotEmpty()
            .WithMessage(RecurringTransactionMessages.CategoryIdRequired);
        RuleFor(command => command.Direction).Must(Enum.IsDefined)
            .WithMessage(RecurringTransactionMessages.DirectionInvalid);
        RuleFor(command => command.Amount).GreaterThan(0m)
            .WithMessage(RecurringTransactionMessages.AmountPositive);
        RuleFor(command => command.Amount).Must(MoneyFitsStorage)
            .When(command => command.Amount > 0m)
            .WithMessage(RecurringTransactionMessages.AmountPrecisionInvalid);
        RuleFor(command => command.Frequency).Must(frequency =>
            Enum.IsDefined(frequency) && frequency is RecurrenceFrequency.Weekly or
                RecurrenceFrequency.Monthly or RecurrenceFrequency.Quarterly or
                RecurrenceFrequency.Yearly)
            .WithMessage(RecurringTransactionMessages.FrequencyInvalid);
        RuleFor(command => command.StartsOn).NotEqual(default(DateOnly))
            .WithMessage(RecurringTransactionMessages.StartsOnRequired);
        RuleFor(command => command).Must(command =>
            !command.EndsOn.HasValue || command.EndsOn >= command.StartsOn)
            .WithMessage(RecurringTransactionMessages.DateRangeInvalid);
        RuleFor(command => command.Description).Must(value => value is null || value.Trim().Length <= 500)
            .WithMessage(RecurringTransactionMessages.DescriptionTooLong);
        RuleFor(command => command.Counterparty).Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage(RecurringTransactionMessages.CounterpartyTooLong);
        RuleFor(command => command.OwnerId).Null()
            .WithMessage(RecurringTransactionMessages.OwnerImmutable);
    }

    private static bool MoneyFitsStorage(decimal amount) =>
        decimal.GetBits(amount)[3] >> 16 <= 4 && Math.Abs(amount) < 1_000_000_000_000_000m;
}
