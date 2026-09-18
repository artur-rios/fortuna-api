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

    [UnitTheory]
    [InlineData("2026-04-15", 30, 31, "2026-04-30", "2026-05-31")]
    [InlineData("2027-02-10", 28, 29, "2027-02-28", "2027-03-29")]
    [InlineData("2027-02-10", 29, 30, "2027-02-28", "2027-03-30")]
    public void GivenDueDayClampedOntoClosingDate_WhenCycleCalculated_ThenDueMovesToNextMonth(
        string date,
        short closingDay,
        short dueDay,
        string expectedClosing,
        string expectedDue)
    {
        var cycle = BillingCycle.Containing(DateOnly.Parse(date), closingDay, dueDay);
        var statement = new CreditCardStatement(Card(), cycle, Now);

        Assert.Equal(DateOnly.Parse(expectedClosing), cycle.ClosingDate);
        Assert.Equal(DateOnly.Parse(expectedDue), cycle.DueDate);
        Assert.True(statement.DueDate > statement.ClosingDate);
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
    public void GivenReconciledInvoiceSummary_WhenApplied_ThenAllStatementFiguresAreRetained()
    {
        var statement = Statement(Card());

        statement.ApplyImportedSummary(
            100m,
            100m,
            165m,
            5m,
            -10m,
            160m,
            Now.AddMinutes(1));

        Assert.Equal(100m, statement.PreviousBalance);
        Assert.Equal(100m, statement.PaymentsReceived);
        Assert.Equal(165m, statement.PurchaseTotal);
        Assert.Equal(5m, statement.ForeignTaxTotal);
        Assert.Equal(-10m, statement.OtherEntries);
        Assert.Equal(160m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenNonReconcilingInvoiceSummary_WhenApplied_ThenItIsRejected()
    {
        var statement = Statement(Card());

        var exception = Assert.Throws<ArgumentException>(() =>
            statement.ApplyImportedSummary(100m, 100m, 165m, 5m, -10m, 161m, Now));

        Assert.Contains("does not reconcile", exception.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenSummaryOffByOneYen_WhenAppliedToYenCard_ThenItReconciles()
    {
        var statement = Statement(Card(new Currency("JPY", "Japanese yen", 0)));

        statement.ApplyImportedSummary(0m, 0m, 1000m, 0m, 0m, 1001m, Now);

        Assert.Equal(1001m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenSummaryOffByTwoCents_WhenAppliedToRealCard_ThenItIsRejected()
    {
        var statement = Statement(Card());

        Assert.Throws<ArgumentException>(() =>
            statement.ApplyImportedSummary(0m, 0m, 100m, 0m, 0m, 100.02m, Now));
    }

    [UnitFact]
    public void GivenImportedSummary_WhenPurchaseTotalRecalculated_ThenImportedAdjustmentIsKept()
    {
        var statement = Statement(Card());
        statement.ApplyImportedSummary(0m, 0m, 100m, 0m, 0m, 100.01m, Now);

        statement.RecalculatePurchaseTotal(110m, Now.AddMinutes(1));

        Assert.Equal(110m, statement.PurchaseTotal);
        Assert.Equal(110.01m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenCreditBalance_WhenPreviousBalanceSet_ThenAmountDueIsReduced()
    {
        var statement = Statement(Card());
        statement.RecalculatePurchaseTotal(100m, Now);

        statement.SetPreviousBalance(-30m, Now.AddMinutes(1));

        Assert.Equal(-30m, statement.PreviousBalance);
        Assert.Equal(70m, statement.AmountDue);
    }

    [UnitFact]
    public void GivenSettledStatement_WhenClosed_ThenItIsRejectedLikeSettlement()
    {
        var card = Card();
        var statement = Statement(card);
        statement.Close(Now);
        statement.Settle(Settlement(card), Now);

        Assert.Throws<InvalidOperationException>(() => statement.Close(Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            statement.Settle(Settlement(card), Now.AddMinutes(1)));
    }

    [UnitFact]
    public void GivenChargeOnSettledStatement_WhenReassigned_ThenItIsRejected()
    {
        var card = Card();
        var settled = Statement(card);
        var charge = new FinancialTransaction(
            card.User,
            card,
            Category(card.User),
            TransactionDirection.Expense,
            10m,
            new DateOnly(2026, 9, 1),
            Now);
        charge.AssignToStatement(settled, false, Now);
        settled.Close(Now);
        settled.Settle(Settlement(card), Now);
        var next = new CreditCardStatement(
            card,
            BillingCycle.Containing(new DateOnly(2026, 10, 10), card.ClosingDay, card.DueDay),
            Now);

        Assert.Throws<InvalidOperationException>(() =>
            charge.AssignToStatement(next, false, Now.AddMinutes(1)));
        Assert.Same(settled, charge.Statement);
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

    private static FinancialTransaction Settlement(CreditCard card) => new(
        card.User,
        card,
        Category(card.User),
        TransactionDirection.Earning,
        1m,
        new DateOnly(2026, 9, 25),
        Now);

    private static CreditCard Card(Currency? currency = null)
    {
        currency ??= new Currency("BRL", "Brazilian real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);

        return new CreditCard(user, "Rewards", "Bank", currency, 1000m, 20, 5, null, Now);
    }
}
