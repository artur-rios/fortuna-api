using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class RecurringTransactionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData(RecurrenceFrequency.Weekly, "2026-02-07")]
    [InlineData(RecurrenceFrequency.Monthly, "2026-02-28")]
    [InlineData(RecurrenceFrequency.Quarterly, "2026-04-30")]
    [InlineData(RecurrenceFrequency.Yearly, "2027-01-31")]
    public void GivenFrequency_WhenOccurrenceCalculated_ThenOriginalAnchorIsUsed(
        RecurrenceFrequency frequency,
        string expected)
    {
        var rule = Rule(frequency, new DateOnly(2026, 1, 31));

        Assert.Equal(DateOnly.Parse(expected), rule.OccurrenceAt(1));
    }

    [UnitFact]
    public void GivenMonthlyMonthEnd_WhenPreviewed_ThenShortMonthsClampWithoutDrift()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 31));

        var dates = rule.NextOccurrences(new DateOnly(2026, 1, 1), 4);

        Assert.Equal(
            [
                new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 28),
                new DateOnly(2026, 3, 31),
                new DateOnly(2026, 4, 30)
            ],
            dates);
    }

    [UnitFact]
    public void GivenPastStartAndEnd_WhenPreviewed_ThenOnlyCurrentBoundedDatesReturn()
    {
        var rule = Rule(
            RecurrenceFrequency.Monthly,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 4, 15));

        var dates = rule.NextOccurrences(new DateOnly(2026, 3, 1));

        Assert.Equal([new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15)], dates);
    }

    [UnitFact]
    public void GivenEndBeforeStart_WhenCreated_ThenRuleIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(
            RecurrenceFrequency.Monthly,
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 1)));
    }

    [UnitFact]
    public void GivenInvalidFrequency_WhenCreated_ThenRuleIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(
            (RecurrenceFrequency)99,
            new DateOnly(2026, 1, 1)));
    }

    private static RecurringTransaction Rule(
        RecurrenceFrequency frequency,
        DateOnly startsOn,
        DateOnly? endsOn = null)
    {
        var user = new UserProfile(
            Guid.NewGuid(),
            "Owner",
            new Currency("BRL", "Brazilian real", 2),
            Now);
        return new RecurringTransaction(
            user,
            new FinancialAccount(
                user,
                "Daily",
                null,
                FinancialAccountType.Checking,
                user.DisplayCurrency,
                0m,
                Now),
            null,
            new Category(user, "Bills", Now),
            TransactionDirection.Expense,
            10m,
            frequency,
            startsOn,
            endsOn,
            Now);
    }
}
