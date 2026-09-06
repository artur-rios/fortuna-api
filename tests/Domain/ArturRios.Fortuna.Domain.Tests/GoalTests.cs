using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class GoalTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidResources_WhenGoalCreated_ThenDetailsAreNormalized()
    {
        var user = User();
        var account = Account(user, "Savings");
        var investment = Investment(user, "Bond");

        var goal = new Goal(
            user, "  Home  ", 100_000m, user.DisplayCurrency,
            new DateOnly(2027, 9, 6), [account, account], [investment, investment], Now);

        Assert.Equal("Home", goal.Name);
        Assert.Equal(100_000m, goal.TargetAmount);
        Assert.Equal("BRL", goal.Currency.Code);
        Assert.Equal(new DateOnly(2027, 9, 6), goal.TargetDate);
        Assert.Same(account, Assert.Single(goal.Accounts));
        Assert.Same(investment, Assert.Single(goal.Investments));
        Assert.Equal(Now, goal.CreatedAt);
    }

    [UnitTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveTarget_WhenGoalCreated_ThenItIsRejected(decimal target)
    {
        var user = User();

        Assert.Throws<ArgumentOutOfRangeException>(() => new Goal(
            user, "Home", target, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [Account(user, "Savings")], [], Now));
    }

    [UnitFact]
    public void GivenMissingResourcesOrFutureDate_WhenGoalCreated_ThenItIsRejected()
    {
        var user = User();
        var account = Account(user, "Savings");

        Assert.Throws<ArgumentException>(() => new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [], [], Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2026, 9, 6), [account], [], Now));
    }

    [UnitFact]
    public void GivenForeignOrDeletedResources_WhenGoalCreated_ThenTheyAreRejected()
    {
        var user = User();
        var foreign = User();
        var deletedAccount = Account(user, "Deleted");
        var deletedInvestment = Investment(user, "Deleted");
        deletedAccount.SoftDelete(Now.AddMinutes(1));
        deletedInvestment.SoftDelete(Now.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [Account(foreign, "Foreign")], [], Now));
        Assert.Throws<ArgumentException>(() => new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [deletedAccount], [], Now));
        Assert.Throws<ArgumentException>(() => new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [], [deletedInvestment], Now));
    }

    [UnitFact]
    public void GivenNewDetails_WhenGoalUpdated_ThenDetailsAndTimestampChange()
    {
        var user = User();
        var goal = new Goal(
            user, "Home", 100m, user.DisplayCurrency,
            new DateOnly(2027, 1, 1), [Account(user, "Before")], [], Now);
        var investment = Investment(user, "After");
        var updatedAt = Now.AddHours(1);

        goal.UpdateDetails(
            "  Retirement  ", 500m, new Currency("USD", "US dollar", 2),
            new DateOnly(2028, 1, 1), [], [investment], updatedAt);

        Assert.Equal("Retirement", goal.Name);
        Assert.Equal(500m, goal.TargetAmount);
        Assert.Equal("USD", goal.Currency.Code);
        Assert.Empty(goal.Accounts);
        Assert.Same(investment, Assert.Single(goal.Investments));
        Assert.Equal(updatedAt, goal.UpdatedAt);
    }

    private static UserProfile User() => new(
        Guid.NewGuid(), "Owner", new Currency("BRL", "Brazilian real", 2), Now);

    private static FinancialAccount Account(UserProfile user, string name) => new(
        user, name, null, FinancialAccountType.Savings, user.DisplayCurrency, 100m, Now);

    private static Investment Investment(UserProfile user, string name) => new(
        user, name, null, InvestmentType.FixedIncome, user.DisplayCurrency, Now);
}
