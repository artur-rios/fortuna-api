using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordTransactionCommandValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly RecordTransactionCommandValidator validator =
        new(new FixedTimeProvider(Now));

    [UnitFact]
    public async Task GivenAccountOrCardTarget_WhenValidated_ThenBothShapesAreAccepted()
    {
        var account = ValidCommand();
        var card = ValidCommand();
        card.FinancialAccountId = null;
        card.CreditCardId = Guid.NewGuid();

        var accountResult = await validator.ValidateAsync(account);
        var cardResult = await validator.ValidateAsync(card);

        Assert.True(accountResult.IsValid);
        Assert.True(cardResult.IsValid);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GivenNonPositiveAmount_WhenValidated_ThenItIsRejected(decimal amount)
    {
        var command = ValidCommand();
        command.Amount = amount;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.AmountPositive);
    }

    [UnitFact]
    public async Task GivenExcessPrecision_WhenValidated_ThenItIsRejected()
    {
        var command = ValidCommand();
        command.Amount = 1.00001m;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.AmountPrecisionInvalid);
    }

    [UnitFact]
    public async Task GivenDateBeyondTomorrow_WhenValidated_ThenItIsRejected()
    {
        var command = ValidCommand();
        command.OccurredOn = new DateOnly(2026, 9, 7);

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.OccurredOnTooFarInFuture);
    }

    [UnitTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task GivenInvalidTargetCount_WhenValidated_ThenItIsRejected(
        bool hasAccount,
        bool hasCard)
    {
        var command = ValidCommand();
        command.FinancialAccountId = hasAccount ? Guid.NewGuid() : null;
        command.CreditCardId = hasCard ? Guid.NewGuid() : null;

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.ExactlyOneTargetRequired);
    }

    [UnitTheory]
    [InlineData("category")]
    [InlineData("direction")]
    [InlineData("owner")]
    public async Task GivenImmutableOrRequiredFieldInvalid_WhenValidated_ThenItIsRejected(
        string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "category" => SetCategoryInvalid(command),
            "direction" => SetDirectionInvalid(command),
            _ => SetOwnerInvalid(command)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item => item.ErrorMessage == expected);
    }

    [UnitTheory]
    [InlineData("description")]
    [InlineData("counterparty")]
    [InlineData("tag")]
    [InlineData("tag-empty")]
    [InlineData("tag-count")]
    [InlineData("currency")]
    public async Task GivenOversizedOrMalformedOptionalField_WhenValidated_ThenItIsRejected(
        string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "description" => SetDescriptionInvalid(command),
            "counterparty" => SetCounterpartyInvalid(command),
            "tag" => SetTagInvalid(command),
            "tag-empty" => SetEmptyTag(command),
            "tag-count" => SetTooManyTags(command),
            _ => SetCurrencyInvalid(command)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item => item.ErrorMessage == expected);
    }

    private static RecordTransactionCommand ValidCommand() => new()
    {
        OccurredOn = new DateOnly(2026, 9, 6),
        Amount = 10m,
        Direction = TransactionDirection.Expense,
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid()
    };

    private static string SetCategoryInvalid(RecordTransactionCommand command)
    {
        command.CategoryId = Guid.Empty;
        return TransactionMessages.CategoryIdRequired;
    }

    private static string SetDirectionInvalid(RecordTransactionCommand command)
    {
        command.Direction = (TransactionDirection)99;
        return TransactionMessages.DirectionInvalid;
    }

    private static string SetOwnerInvalid(RecordTransactionCommand command)
    {
        command.OwnerId = Guid.NewGuid();
        return TransactionMessages.OwnerImmutable;
    }

    private static string SetDescriptionInvalid(RecordTransactionCommand command)
    {
        command.Description = new string('x', 501);
        return TransactionMessages.DescriptionTooLong;
    }

    private static string SetCounterpartyInvalid(RecordTransactionCommand command)
    {
        command.Counterparty = new string('x', 201);
        return TransactionMessages.CounterpartyTooLong;
    }

    private static string SetTagInvalid(RecordTransactionCommand command)
    {
        command.Tags = [new string('x', 201)];
        return TransactionMessages.TagTooLong;
    }

    private static string SetEmptyTag(RecordTransactionCommand command)
    {
        command.Tags = [string.Empty];
        return TransactionMessages.TagRequired;
    }

    private static string SetTooManyTags(RecordTransactionCommand command)
    {
        command.Tags = Enumerable.Range(1, 51).Select(index => $"Tag {index}").ToArray();
        return TransactionMessages.TooManyTags;
    }

    private static string SetCurrencyInvalid(RecordTransactionCommand command)
    {
        command.CurrencyCode = "US";
        return TransactionMessages.CurrencyInvalid;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
