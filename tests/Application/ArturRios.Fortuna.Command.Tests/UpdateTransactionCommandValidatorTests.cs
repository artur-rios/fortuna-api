using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateTransactionCommandValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly UpdateTransactionCommandValidator validator =
        new(new FixedTimeProvider(Now));

    [UnitFact]
    public async Task GivenValidEditableFields_WhenValidated_ThenCommandIsAccepted()
    {
        var result = await validator.ValidateAsync(ValidCommand());

        Assert.True(result.IsValid);
    }

    [UnitTheory]
    [InlineData("id")]
    [InlineData("date")]
    [InlineData("future-date")]
    [InlineData("amount")]
    [InlineData("precision")]
    [InlineData("direction")]
    [InlineData("category")]
    public async Task GivenInvalidRequiredField_WhenValidated_ThenItIsRejected(string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "id" => SetInvalidId(command),
            "date" => SetInvalidDate(command),
            "future-date" => SetFutureDate(command),
            "amount" => SetInvalidAmount(command),
            "precision" => SetInvalidPrecision(command),
            "direction" => SetInvalidDirection(command),
            _ => SetInvalidCategory(command)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item => item.ErrorMessage == expected);
    }

    [UnitTheory]
    [InlineData("account")]
    [InlineData("card")]
    [InlineData("currency")]
    [InlineData("owner")]
    public async Task GivenImmutableField_WhenValidated_ThenItIsRejected(string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "account" => SetAccount(command),
            "card" => SetCard(command),
            "currency" => SetCurrency(command),
            _ => SetOwner(command)
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
    public async Task GivenInvalidOptionalField_WhenValidated_ThenItIsRejected(string field)
    {
        var command = ValidCommand();
        var expected = field switch
        {
            "description" => SetDescription(command),
            "counterparty" => SetCounterparty(command),
            "tag" => SetTag(command),
            "tag-empty" => SetEmptyTag(command),
            _ => SetTooManyTags(command)
        };

        var result = await validator.ValidateAsync(command);

        Assert.Contains(result.Errors, item => item.ErrorMessage == expected);
    }

    private static UpdateTransactionCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        OccurredOn = new DateOnly(2026, 9, 6),
        Amount = 10m,
        Direction = TransactionDirection.Expense,
        CategoryId = Guid.NewGuid()
    };

    private static string SetInvalidId(UpdateTransactionCommand command)
    {
        command.Id = Guid.Empty;
        return TransactionMessages.TransactionIdRequired;
    }

    private static string SetInvalidDate(UpdateTransactionCommand command)
    {
        command.OccurredOn = default;
        return TransactionMessages.OccurredOnRequired;
    }

    private static string SetFutureDate(UpdateTransactionCommand command)
    {
        command.OccurredOn = new DateOnly(2026, 9, 7);
        return TransactionMessages.OccurredOnTooFarInFuture;
    }

    private static string SetInvalidAmount(UpdateTransactionCommand command)
    {
        command.Amount = 0m;
        return TransactionMessages.AmountPositive;
    }

    private static string SetInvalidPrecision(UpdateTransactionCommand command)
    {
        command.Amount = 1.00001m;
        return TransactionMessages.AmountPrecisionInvalid;
    }

    private static string SetInvalidDirection(UpdateTransactionCommand command)
    {
        command.Direction = (TransactionDirection)99;
        return TransactionMessages.DirectionInvalid;
    }

    private static string SetInvalidCategory(UpdateTransactionCommand command)
    {
        command.CategoryId = Guid.Empty;
        return TransactionMessages.CategoryIdRequired;
    }

    private static string SetAccount(UpdateTransactionCommand command)
    {
        command.FinancialAccountId = Guid.NewGuid();
        return TransactionMessages.TransactionTargetImmutable;
    }

    private static string SetCard(UpdateTransactionCommand command)
    {
        command.CreditCardId = Guid.NewGuid();
        return TransactionMessages.TransactionTargetImmutable;
    }

    private static string SetCurrency(UpdateTransactionCommand command)
    {
        command.CurrencyCode = "USD";
        return TransactionMessages.TransactionCurrencyImmutable;
    }

    private static string SetOwner(UpdateTransactionCommand command)
    {
        command.OwnerId = Guid.NewGuid();
        return TransactionMessages.OwnerImmutable;
    }

    private static string SetDescription(UpdateTransactionCommand command)
    {
        command.Description = new string('d', 501);
        return TransactionMessages.DescriptionTooLong;
    }

    private static string SetCounterparty(UpdateTransactionCommand command)
    {
        command.Counterparty = new string('c', 201);
        return TransactionMessages.CounterpartyTooLong;
    }

    private static string SetTag(UpdateTransactionCommand command)
    {
        command.Tags = [new string('t', 201)];
        return TransactionMessages.TagTooLong;
    }

    private static string SetEmptyTag(UpdateTransactionCommand command)
    {
        command.Tags = [string.Empty];
        return TransactionMessages.TagRequired;
    }

    private static string SetTooManyTags(UpdateTransactionCommand command)
    {
        command.Tags = Enumerable.Range(1, 51).Select(index => $"Tag {index}").ToArray();
        return TransactionMessages.TooManyTags;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
