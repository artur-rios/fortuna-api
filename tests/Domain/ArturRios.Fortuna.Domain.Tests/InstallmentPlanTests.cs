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
        var amounts = InstallmentPlan.Split(100m, 3, 2);

        Assert.Equal([33.34m, 33.33m, 33.33m], amounts);
        Assert.Equal(100m, amounts.Sum());
    }

    [UnitFact]
    public void GivenWholeUnitCurrency_WhenSplit_ThenPartsUseItsMinorUnit()
    {
        var amounts = InstallmentPlan.Split(100m, 6, 0);

        Assert.Equal([20m, 16m, 16m, 16m, 16m, 16m], amounts);
        Assert.Equal(100m, amounts.Sum());
    }

    [UnitTheory]
    [InlineData("0", 2)]
    [InlineData("10", 1)]
    [InlineData("0.01", 2)]
    public void GivenInvalidSplit_WhenCalculated_ThenItIsRejected(
        string total,
        short count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InstallmentPlan.Split(decimal.Parse(total), count, 2));
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
