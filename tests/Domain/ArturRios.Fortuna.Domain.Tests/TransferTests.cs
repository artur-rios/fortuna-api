using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class TransferTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenPairedMovements_WhenCreated_ThenTransferRetainsConversion()
    {
        var user = User();
        var outbound = AccountMovement(user, TransactionDirection.Expense);
        var inbound = CardMovement(user, TransactionDirection.Earning);

        var transfer = new Transfer(
            outbound,
            inbound,
            5m,
            new DateOnly(2026, 9, 3),
            Now);

        Assert.Equal(outbound, transfer.OutboundTransaction);
        Assert.Equal(inbound, transfer.InboundTransaction);
        Assert.Equal(5m, transfer.AppliedRate);
        Assert.Equal(new DateOnly(2026, 9, 3), transfer.RateDate);
    }

    [UnitFact]
    public void GivenSameMovementTwice_WhenCreated_ThenTransferIsRejected()
    {
        var movement = AccountMovement(User(), TransactionDirection.Expense);

        Assert.Throws<ArgumentException>(() =>
            new Transfer(movement, movement, null, null, Now));
    }

    [UnitFact]
    public void GivenDifferentOwners_WhenCreated_ThenTransferIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Transfer(
            AccountMovement(User(), TransactionDirection.Expense),
            CardMovement(User(), TransactionDirection.Earning),
            null,
            null,
            Now));
    }

    [UnitTheory]
    [InlineData(TransactionDirection.Earning, TransactionDirection.Earning)]
    [InlineData(TransactionDirection.Expense, TransactionDirection.Expense)]
    public void GivenInvalidDirections_WhenCreated_ThenTransferIsRejected(
        TransactionDirection outboundDirection,
        TransactionDirection inboundDirection)
    {
        var user = User();

        Assert.Throws<ArgumentException>(() => new Transfer(
            AccountMovement(user, outboundDirection),
            CardMovement(user, inboundDirection),
            null,
            null,
            Now));
    }

    [UnitTheory]
    [InlineData(null, "2026-09-03")]
    [InlineData("0", "2026-09-03")]
    [InlineData("1", null)]
    public void GivenIncompleteConversion_WhenCreated_ThenTransferIsRejected(
        string? rate,
        string? date)
    {
        var user = User();

        Assert.Throws<ArgumentException>(() => new Transfer(
            AccountMovement(user, TransactionDirection.Expense),
            CardMovement(user, TransactionDirection.Earning),
            rate is null ? null : decimal.Parse(rate),
            date is null ? null : DateOnly.Parse(date),
            Now));
    }

    private static FinancialTransaction AccountMovement(
        UserProfile user,
        TransactionDirection direction) => new(
        user,
        new FinancialAccount(
            user,
            "Daily",
            null,
            FinancialAccountType.Checking,
            user.DisplayCurrency,
            0m,
            Now),
        direction,
        10m,
        new DateOnly(2026, 9, 4),
        Now);

    private static FinancialTransaction CardMovement(
        UserProfile user,
        TransactionDirection direction) => new(
        user,
        new CreditCard(
            user,
            "Rewards",
            "Bank",
            user.DisplayCurrency,
            1000m,
            20,
            5,
            null,
            Now),
        direction,
        10m,
        new DateOnly(2026, 9, 4),
        Now);

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian real", 2),
        Now);
}
