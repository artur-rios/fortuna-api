using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class MaterializeRecurringTransactionsCommandValidatorTests
{
    [UnitFact]
    public void GivenEmptyCommand_WhenValidated_ThenItPasses()
    {
        var result = new MaterializeRecurringTransactionsCommandValidator().Validate(
            new MaterializeRecurringTransactionsCommand());

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public void GivenSuppliedOwner_WhenValidated_ThenImmutableOwnerIsReported()
    {
        var result = new MaterializeRecurringTransactionsCommandValidator().Validate(
            new MaterializeRecurringTransactionsCommand
            {
                OwnerId = Guid.NewGuid()
            });

        Assert.Contains(
            RecurringTransactionMessages.OwnerImmutable,
            result.Errors.Select(error => error.ErrorMessage));
    }
}
