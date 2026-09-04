using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class FinancialTransactionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidMovement_WhenCreated_ThenAccountLedgerFieldsAreFixed()
    {
        var user = User();
        var account = Account(user);
        var occurredOn = new DateOnly(2026, 9, 3);

        var transaction = new FinancialTransaction(
            user,
            account,
            TransactionDirection.Expense,
            12.3456m,
            occurredOn,
            Now);

        Assert.Equal(user, transaction.User);
        Assert.Equal(account, transaction.FinancialAccount);
        Assert.Equal(TransactionDirection.Expense, transaction.Direction);
        Assert.Equal(12.3456m, transaction.Amount);
        Assert.Equal(occurredOn, transaction.OccurredOn);
        Assert.False(transaction.IsDeleted);
        Assert.Null(transaction.CreditCard);
    }

    [UnitFact]
    public void GivenValidCardCharge_WhenCreated_ThenCardLedgerFieldsAreFixed()
    {
        var user = User();
        var card = Card(user);
        var occurredOn = new DateOnly(2026, 9, 3);

        var transaction = new FinancialTransaction(
            user,
            card,
            TransactionDirection.Expense,
            99.99m,
            occurredOn,
            Now);

        Assert.Equal(user, transaction.User);
        Assert.Equal(card, transaction.CreditCard);
        Assert.Null(transaction.FinancialAccount);
        Assert.Equal(TransactionDirection.Expense, transaction.Direction);
        Assert.Equal(99.99m, transaction.Amount);
        Assert.Equal(occurredOn, transaction.OccurredOn);
    }

    [UnitFact]
    public void GivenDifferentCardOwner_WhenCreated_ThenTransactionIsRejected()
    {
        var owner = User();
        var other = User();

        var exception = Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            other,
            Card(owner),
            TransactionDirection.Expense,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("card", exception.ParamName);
    }

    [UnitFact]
    public void GivenDifferentOwner_WhenCreated_ThenTransactionIsRejected()
    {
        var owner = User();
        var other = User();

        var exception = Assert.Throws<ArgumentException>(() => new FinancialTransaction(
            other,
            Account(owner),
            TransactionDirection.Earning,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("account", exception.ParamName);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveAmount_WhenCreated_ThenTransactionIsRejected(decimal amount)
    {
        var user = User();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialTransaction(
            user,
            Account(user),
            TransactionDirection.Earning,
            amount,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("amount", exception.ParamName);
    }

    [UnitFact]
    public void GivenUnknownDirection_WhenCreated_ThenTransactionIsRejected()
    {
        var user = User();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new FinancialTransaction(
            user,
            Account(user),
            (TransactionDirection)999,
            1m,
            new DateOnly(2026, 9, 4),
            Now));

        Assert.Equal("direction", exception.ParamName);
    }

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Account Owner",
        Currency(),
        Now);

    private static FinancialAccount Account(UserProfile user) => new(
        user,
        "Daily",
        null,
        FinancialAccountType.Checking,
        Currency(),
        0m,
        Now);

    private static CreditCard Card(UserProfile user) => new(
        user,
        "Rewards",
        "Example Bank",
        Currency(),
        1000m,
        20,
        5,
        null,
        Now);

    private static Currency Currency() => new("BRL", "Brazilian real", 2);
}
