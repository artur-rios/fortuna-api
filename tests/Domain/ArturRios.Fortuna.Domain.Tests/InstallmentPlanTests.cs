using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class InstallmentPlanTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenUnevenTotal_WhenSplit_ThenRemainderIsAssignedToFirstInstallment()
    {
        var split = InstallmentPlan.TrySplit(100m, 3, 2);
        var amounts = split.Amounts;

        Assert.True(split.Succeeded);
        Assert.Equal([33.34m, 33.33m, 33.33m], amounts);
        Assert.Equal(100m, amounts.Sum());
    }

    [UnitFact]
    public void GivenWholeUnitCurrency_WhenSplit_ThenPartsUseItsMinorUnit()
    {
        var amounts = InstallmentPlan.TrySplit(100m, 6, 0).Amounts;

        Assert.Equal([20m, 16m, 16m, 16m, 16m, 16m], amounts);
        Assert.Equal(100m, amounts.Sum());
    }

    [UnitTheory]
    [InlineData("100.555", 2, 2, "100.56")]
    [InlineData("100.5", 3, 0, "101")]
    public void GivenTotalFinerThanMinorUnit_WhenSplit_ThenEveryPartFitsTheCurrencyScale(
        string total,
        short count,
        short digits,
        string expectedSum)
    {
        var amounts = InstallmentPlan.TrySplit(decimal.Parse(total), count, digits).Amounts;

        Assert.All(amounts, amount => Assert.Equal(decimal.Round(amount, digits), amount));
        Assert.Equal(decimal.Parse(expectedSum), amounts.Sum());
    }

    [UnitTheory]
    [InlineData("0", 2, InstallmentSplitOutcome.TotalNotPositive)]
    [InlineData("10", 1, InstallmentSplitOutcome.TooFewInstallments)]
    [InlineData("0.01", 2, InstallmentSplitOutcome.AmountTooSmall)]
    public void GivenInvalidSplit_WhenCalculated_ThenItIsRejected(
        string total,
        short count,
        InstallmentSplitOutcome expected)
    {
        var split = InstallmentPlan.TrySplit(decimal.Parse(total), count, 2);

        Assert.False(split.Succeeded);
        Assert.Equal(expected, split.Outcome);
        Assert.Empty(split.Amounts);
    }

    [UnitFact]
    public void GivenUnsupportedMinorUnit_WhenSplit_ThenItIsRejected()
    {
        var split = InstallmentPlan.TrySplit(100m, 2, 5);

        Assert.Equal(InstallmentSplitOutcome.MinorUnitDigitsUnsupported, split.Outcome);
    }

    [UnitFact]
    public void GivenValidExpense_WhenAdded_ThenItIsLinkedAndNumbered()
    {
        var user = User();
        var card = Card(user);
        var plan = new InstallmentPlan(card, 100m, 3, new DateOnly(2026, 9, 5), Now);
        var transaction = new FinancialTransaction(
            user,
            card,
            new Category(user, "Shopping", Now),
            TransactionDirection.Expense,
            33.34m,
            new DateOnly(2026, 9, 5),
            Now);

        plan.AddInstallment(transaction, 1, Now);

        Assert.Same(plan, transaction.InstallmentPlan);
        Assert.Equal((short)1, transaction.InstallmentNumber);
        Assert.Contains(transaction, plan.Installments);
    }

    [UnitFact]
    public void GivenDuplicateNumber_WhenAdded_ThenItIsRejected()
    {
        var user = User();
        var card = Card(user);
        var category = new Category(user, "Shopping", Now);
        var plan = new InstallmentPlan(card, 100m, 2, new DateOnly(2026, 9, 5), Now);
        plan.AddInstallment(Transaction(user, card, category), 1, Now);

        Assert.Throws<ArgumentException>(() =>
            plan.AddInstallment(Transaction(user, card, category), 1, Now));
    }

    private static FinancialTransaction Transaction(
        UserProfile user,
        CreditCard card,
        Category category) => new(
        user,
        card,
        category,
        TransactionDirection.Expense,
        50m,
        new DateOnly(2026, 9, 5),
        Now);

    private static CreditCard Card(UserProfile user) => new(
        user,
        "Rewards",
        "Bank",
        user.DisplayCurrency,
        1000m,
        20,
        5,
        null,
        Now);

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian real", 2),
        Now);
}
