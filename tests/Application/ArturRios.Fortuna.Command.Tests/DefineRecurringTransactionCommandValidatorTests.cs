using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class DefineRecurringTransactionCommandValidatorTests
{
    [UnitFact]
    public void GivenValidPastStart_WhenValidated_ThenItPasses()
    {
        var result = new DefineRecurringTransactionCommandValidator().Validate(
            new DefineRecurringTransactionCommand
            {
                FinancialAccountId = Guid.NewGuid(),
                CategoryId = Guid.NewGuid(),
                Direction = TransactionDirection.Expense,
                Amount = 10m,
                Frequency = RecurrenceFrequency.Monthly,
                StartsOn = new DateOnly(2020, 1, 31)
            });

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenInvalidRule_WhenValidated_ThenEveryInvalidFieldIsReported()
    {
        var result = new DefineRecurringTransactionCommandValidator().Validate(
            new DefineRecurringTransactionCommand
            {
                FinancialAccountId = Guid.NewGuid(),
                CreditCardId = Guid.NewGuid(),
                Amount = 0m,
                Frequency = (RecurrenceFrequency)99,
                StartsOn = new DateOnly(2026, 2, 1),
                EndsOn = new DateOnly(2026, 1, 1),
                OwnerId = Guid.NewGuid()
            });

        var messages = result.Errors.Select(item => item.ErrorMessage).ToArray();
        Assert.Contains(RecurringTransactionMessages.ExactlyOneTargetRequired, messages);
        Assert.Contains(RecurringTransactionMessages.CategoryIdRequired, messages);
        Assert.Contains(RecurringTransactionMessages.DirectionInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.AmountPositive, messages);
        Assert.Contains(RecurringTransactionMessages.FrequencyInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.DateRangeInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.OwnerImmutable, messages);
    }
}
