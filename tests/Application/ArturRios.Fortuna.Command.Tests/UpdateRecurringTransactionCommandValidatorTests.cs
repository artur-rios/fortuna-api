using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateRecurringTransactionCommandValidatorTests
{
    [UnitFact]
    public void GivenValidReplacementTemplate_WhenValidated_ThenItPasses()
    {
        var result = new UpdateRecurringTransactionCommandValidator().Validate(ValidCommand());

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenInvalidReplacementTemplate_WhenValidated_ThenEveryInvalidFieldIsReported()
    {
        var result = new UpdateRecurringTransactionCommandValidator().Validate(
            new UpdateRecurringTransactionCommand
            {
                FinancialAccountId = Guid.NewGuid(),
                CreditCardId = Guid.NewGuid(),
                Amount = 0m,
                Frequency = (RecurrenceFrequency)99,
                StartsOn = new DateOnly(2026, 2, 1),
                EndsOn = new DateOnly(2026, 1, 1),
                OwnerId = Guid.NewGuid()
            });

        var messages = result.Errors.Select(error => error.ErrorMessage).ToArray();
        Assert.Contains(RecurringTransactionMessages.IdRequired, messages);
        Assert.Contains(RecurringTransactionMessages.ExactlyOneTargetRequired, messages);
        Assert.Contains(RecurringTransactionMessages.CategoryIdRequired, messages);
        Assert.Contains(RecurringTransactionMessages.DirectionInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.AmountPositive, messages);
        Assert.Contains(RecurringTransactionMessages.FrequencyInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.DateRangeInvalid, messages);
        Assert.Contains(RecurringTransactionMessages.OwnerImmutable, messages);
    }

    private static UpdateRecurringTransactionCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 10m,
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 1, 1)
    };
}
