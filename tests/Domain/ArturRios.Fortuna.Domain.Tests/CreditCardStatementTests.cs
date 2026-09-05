using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class CreditCardStatementTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 20, 0, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData("2026-09-10", "2026-08-21", "2026-09-20", "2026-10-05")]
    [InlineData("2026-09-21", "2026-09-21", "2026-10-20", "2026-11-05")]
    public void GivenChargeDate_WhenCycleCalculated_ThenClosingAnchorDefinesPeriod(
        string date,
        string expectedStart,
        string expectedEnd,
        string expectedDue)
    {
        var cycle = BillingCycle.Containing(DateOnly.Parse(date), 20, 5);

        Assert.Equal(DateOnly.Parse(expectedStart), cycle.PeriodStart);
        Assert.Equal(DateOnly.Parse(expectedEnd), cycle.PeriodEnd);
        Assert.Equal(cycle.PeriodEnd, cycle.ClosingDate);
        Assert.Equal(DateOnly.Parse(expectedDue), cycle.DueDate);
    }

    [UnitFact]
    public void GivenClosingDayBeyondFebruary_WhenCycleCalculated_ThenMonthEndIsUsed()
    {
        var cycle = BillingCycle.Containing(new DateOnly(2027, 2, 20), 31, 5);

        Assert.Equal(new DateOnly(2027, 2, 1), cycle.PeriodStart);
        Assert.Equal(new DateOnly(2027, 2, 28), cycle.PeriodEnd);
        Assert.Equal(new DateOnly(2027, 3, 5), cycle.DueDate);
    }

    [UnitFact]
    public void GivenCharge_WhenAssigned_ThenStatementTotalAndLinkAreUpdated()
    {
        var card = Card();
        var statement = Statement(card);
        var charge = new FinancialTransaction(
            card.User,
            card,
            Category(card.User),
            TransactionDirection.Expense,
            125.50m,
            new DateOnly(2026, 9, 10),
            Now);

        charge.AssignToStatement(statement, isLateArriving: true, Now.AddMinutes(1));
        statement.RecalculatePurchaseTotal(125.50m, Now.AddMinutes(1));

        Assert.Equal(statement, charge.Statement);
        Assert.True(charge.IsLateArriving);
        Assert.Equal(125.50m, statement.PurchaseTotal);
        Assert.Equal(125.50m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenSettledStatement_WhenCompositionChanges_ThenItIsRejected()
    {
        var card = Card();
        var statement = Statement(card);
        var settlement = new FinancialTransaction(
            card.User,
            card,
            Category(card.User),
            TransactionDirection.Earning,
            100m,
            new DateOnly(2026, 9, 25),
            Now);
        statement.Close(Now.AddMinutes(1));
        statement.Settle(settlement, Now.AddMinutes(2));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            statement.RecalculatePurchaseTotal(100m, Now.AddMinutes(3)));

        Assert.Contains("frozen", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenDifferentCard_WhenChargeAssigned_ThenItIsRejected()
    {
        var chargeCard = Card();
        var otherStatement = Statement(Card());
        var charge = new FinancialTransaction(
            chargeCard.User,
            chargeCard,
            Category(chargeCard.User),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 1),
            Now);

        Assert.Throws<ArgumentException>(() =>
            charge.AssignToStatement(otherStatement, false, Now));
    }

    [UnitFact]
    public void GivenPartialSettlement_WhenBalanceCarried_ThenNextAmountDueIncludesIt()
    {
        var statement = Statement(Card());
        statement.RecalculatePurchaseTotal(25m, Now);

        statement.SetPreviousBalance(75m, Now.AddMinutes(1));

        Assert.Equal(75m, statement.PreviousBalance);
        Assert.Equal(100m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenSettledStatement_WhenBalanceCarried_ThenItIsRejected()
    {
        var card = Card();
        var statement = Statement(card);
        var settlement = new FinancialTransaction(
            card.User,
            card,
            Category(card.User),
            TransactionDirection.Earning,
            1m,
            new DateOnly(2026, 9, 25),
            Now);
        statement.Close(Now);
        statement.Settle(settlement, Now);

        Assert.Throws<InvalidOperationException>(() =>
            statement.SetPreviousBalance(1m, Now));
    }

    [UnitFact]
    public void GivenOutboundMovement_WhenStatementSettled_ThenItIsRejected()
    {
        var card = Card();
        var statement = Statement(card);
        var settlement = new FinancialTransaction(
            card.User,
            card,
            Category(card.User),
            TransactionDirection.Expense,
            1m,
            new DateOnly(2026, 9, 25),
            Now);
        statement.Close(Now);

        Assert.Throws<ArgumentException>(() => statement.Settle(settlement, Now));
    }

    [UnitFact]
    public void GivenOtherCardMovement_WhenStatementSettled_ThenItIsRejected()
    {
        var card = Card();
        var otherCard = new CreditCard(
            card.User,
            "Travel",
            "Bank",
            card.Currency,
            1000m,
            20,
            5,
            null,
            Now);
        var statement = Statement(card);
        var settlement = new FinancialTransaction(
            card.User,
            otherCard,
            Category(card.User),
            TransactionDirection.Earning,
            1m,
            new DateOnly(2026, 9, 25),
            Now);
        statement.Close(Now);

        Assert.Throws<ArgumentException>(() => statement.Settle(settlement, Now));
    }

    private static CreditCardStatement Statement(CreditCard card) => new(
        card,
        BillingCycle.Containing(new DateOnly(2026, 9, 10), card.ClosingDay, card.DueDay),
        Now);

    private static Category Category(UserProfile user) => new(user, "General", Now);

    private static CreditCard Card()
    {
        var currency = new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        return new CreditCard(user, "Rewards", "Bank", currency, 1000m, 20, 5, null, Now);
    }
}
