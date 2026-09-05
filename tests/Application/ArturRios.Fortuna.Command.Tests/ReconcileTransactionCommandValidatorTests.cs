using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ReconcileTransactionCommandValidatorTests
{
    private readonly ReconcileTransactionCommandValidator validator = new();

    [UnitFact]
    public async Task GivenRecordReference_WhenReconciling_ThenCommandIsAccepted()
    {
        var result = await validator.ValidateAsync(ValidCommand());

        Assert.True(result.IsValid);
    }

    [UnitFact]
    public async Task GivenNoRecordReference_WhenUnreconciling_ThenCommandIsAccepted()
    {
        var result = await validator.ValidateAsync(new ReconcileTransactionCommand
        {
            Id = Guid.NewGuid(),
            Unreconcile = true
        });

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData("transaction")]
    [InlineData("job")]
    [InlineData("record")]
    public async Task GivenMissingReconciliationReference_WhenValidated_ThenItIsRejected(
        string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "transaction" => SetTransaction(command, Guid.Empty),
            "job" => SetJob(command, null),
            _ => SetRecord(command, 0)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error => error.ErrorMessage == expected);
    }

    [UnitFact]
    public async Task GivenRecordReference_WhenUnreconciling_ThenItIsRejected()
    {
        var command = ValidCommand();
        command.Unreconcile = true;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransactionMessages.UnreconcileReferencesForbidden);
    }

    private static ReconcileTransactionCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        ImportJobId = Guid.NewGuid(),
        ImportedRecordId = 1
    };

    private static string SetTransaction(ReconcileTransactionCommand command, Guid id)
    {
        command.Id = id;
        return TransactionMessages.TransactionIdRequired;
    }

    private static string SetJob(ReconcileTransactionCommand command, Guid? id)
    {
        command.ImportJobId = id;
        return TransactionMessages.ImportJobIdRequired;
    }

    private static string SetRecord(ReconcileTransactionCommand command, long? id)
    {
        command.ImportedRecordId = id;
        return TransactionMessages.ImportedRecordIdRequired;
    }
}
