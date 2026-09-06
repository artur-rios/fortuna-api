using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class BudgetTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidDetails_WhenBudgetCreated_ThenDetailsAreRetained()
    {
        var user = User();
        var category = new Category(user, "Dining", Now);

        var budget = new Budget(
            user,
            500m,
            user.DisplayCurrency,
            BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1),
            [category, category],
            true,
            Now);

        Assert.Equal(user, budget.User);
        Assert.Equal(500m, budget.Amount);
        Assert.Equal("BRL", budget.Currency.Code);
        Assert.Equal(BudgetPeriodType.Monthly, budget.PeriodType);
        Assert.Equal(new DateOnly(2026, 9, 1), budget.PeriodStart);
        Assert.True(budget.IncludeDescendants);
        Assert.Same(category, Assert.Single(budget.Categories));
        Assert.Equal(Now, budget.CreatedAt);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveAmount_WhenBudgetCreated_ThenItIsRejected(decimal amount)
    {
        var user = User();

        Assert.Throws<ArgumentOutOfRangeException>(() => new Budget(
            user,
            amount,
            user.DisplayCurrency,
            BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1),
            [new Category(user, "Dining", Now)],
            true,
            Now));
    }

    [UnitFact]
    public void GivenInvalidPeriodOrCategories_WhenBudgetCreated_ThenItIsRejected()
    {
        var user = User();
        var category = new Category(user, "Dining", Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Budget(
            user, 100m, user.DisplayCurrency, (BudgetPeriodType)99,
            new DateOnly(2026, 9, 1), [category], true, Now));
        Assert.Throws<ArgumentException>(() => new Budget(
            user, 100m, user.DisplayCurrency, BudgetPeriodType.Monthly,
            default, [category], true, Now));
        Assert.Throws<ArgumentException>(() => new Budget(
            user, 100m, user.DisplayCurrency, BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1), [], true, Now));
    }

    [UnitFact]
    public void GivenForeignOrDeletedCategory_WhenBudgetCreated_ThenItIsRejected()
    {
        var user = User();
        var foreign = new Category(User(), "Foreign", Now);
        var deleted = new Category(user, "Deleted", Now);
        deleted.SoftDelete(Now.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => new Budget(
            user, 100m, user.DisplayCurrency, BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1), [foreign], true, Now));
        Assert.Throws<ArgumentException>(() => new Budget(
            user, 100m, user.DisplayCurrency, BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1), [deleted], true, Now));
    }

    [UnitFact]
    public void GivenNewDetails_WhenBudgetUpdated_ThenDetailsAndTimestampChange()
    {
        var user = User();
        var original = new Category(user, "Dining", Now);
        var replacement = new Category(user, "Groceries", Now);
        var budget = new Budget(
            user, 100m, user.DisplayCurrency, BudgetPeriodType.Monthly,
            new DateOnly(2026, 9, 1), [original], true, Now);
        var updatedAt = Now.AddHours(1);

        budget.UpdateDetails(
            300m,
            new Currency("USD", "US dollar", 2),
            BudgetPeriodType.Quarterly,
            new DateOnly(2026, 7, 1),
            [replacement],
            false,
            updatedAt);

        Assert.Equal(300m, budget.Amount);
        Assert.Equal("USD", budget.Currency.Code);
        Assert.Equal(BudgetPeriodType.Quarterly, budget.PeriodType);
        Assert.Equal(new DateOnly(2026, 7, 1), budget.PeriodStart);
        Assert.False(budget.IncludeDescendants);
        Assert.Same(replacement, Assert.Single(budget.Categories));
        Assert.Equal(updatedAt, budget.UpdatedAt);
    }

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Owner",
        new Currency("BRL", "Brazilian real", 2),
        Now);
}
