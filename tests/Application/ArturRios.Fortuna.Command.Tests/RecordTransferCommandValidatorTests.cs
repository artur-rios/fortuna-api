using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordTransferCommandValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly RecordTransferCommandValidator validator =
        new(new FixedTimeProvider(Now));

    [UnitFact]
    public async Task GivenAccountOrStatementDestination_WhenValidated_ThenBothAreAccepted()
    {
        var account = ValidCommand();
        var statement = ValidCommand();
        statement.DestinationFinancialAccountId = null;
        statement.DestinationStatementId = Guid.NewGuid();

        var accountResult = await validator.ValidateAsync(account);
        var statementResult = await validator.ValidateAsync(statement);

        Assert.True(accountResult.IsValid);
        Assert.True(statementResult.IsValid);
    }

    [UnitTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task GivenInvalidDestinationCount_WhenValidated_ThenItIsRejected(
        bool hasAccount,
        bool hasStatement)
    {
        var command = ValidCommand();
        command.DestinationFinancialAccountId = hasAccount ? Guid.NewGuid() : null;
        command.DestinationStatementId = hasStatement ? Guid.NewGuid() : null;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransferMessages.ExactlyOneDestinationRequired);
    }

    [UnitFact]
    public async Task GivenSameOriginAndDestination_WhenValidated_ThenItIsRejected()
    {
        var command = ValidCommand();
        command.DestinationFinancialAccountId = command.OriginFinancialAccountId;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == TransferMessages.AccountsMustDiffer);
    }

    [UnitTheory]
    [InlineData("origin")]
    [InlineData("amount")]
    [InlineData("precision")]
    [InlineData("date")]
    [InlineData("future")]
    [InlineData("owner")]
    public async Task GivenInvalidCoreField_WhenValidated_ThenCanonicalErrorIsReturned(
        string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "origin" => InvalidateOrigin(command),
            "amount" => InvalidateAmount(command),
            "precision" => InvalidatePrecision(command),
            "date" => InvalidateDate(command),
            "future" => InvalidateFutureDate(command),
            _ => InvalidateOwner(command)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, error => error.ErrorMessage == expected);
    }

    private static RecordTransferCommand ValidCommand() => new()
    {
        OriginFinancialAccountId = Guid.NewGuid(),
        DestinationFinancialAccountId = Guid.NewGuid(),
        Amount = 10m,
        OccurredOn = new DateOnly(2026, 9, 6)
    };

    private static string InvalidateOrigin(RecordTransferCommand command)
    {
        command.OriginFinancialAccountId = Guid.Empty;
        return TransferMessages.OriginFinancialAccountIdRequired;
    }

    private static string InvalidateAmount(RecordTransferCommand command)
    {
        command.Amount = 0m;
        return TransferMessages.AmountPositive;
    }

    private static string InvalidatePrecision(RecordTransferCommand command)
    {
        command.Amount = 1.00001m;
        return TransferMessages.AmountPrecisionInvalid;
    }

    private static string InvalidateDate(RecordTransferCommand command)
    {
        command.OccurredOn = default;
        return TransferMessages.OccurredOnRequired;
    }

    private static string InvalidateFutureDate(RecordTransferCommand command)
    {
        command.OccurredOn = new DateOnly(2026, 9, 7);
        return TransferMessages.OccurredOnTooFarInFuture;
    }

    private static string InvalidateOwner(RecordTransferCommand command)
    {
        command.OwnerId = Guid.NewGuid();
        return TransferMessages.OwnerImmutable;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
