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

    [UnitFact]
    public void GivenBoundedMonthlyRule_WhenOccurrencesRequested_ThenOnlyDatesInWindowReturn()
    {
        var rule = Rule(
            RecurrenceFrequency.Monthly,
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 4, 30));

        var dates = rule.OccurrencesBetween(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 5, 31));

        Assert.Equal(
            [new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)],
            dates);
    }

    [UnitFact]
    public void GivenWindowBeforeStart_WhenOccurrencesRequested_ThenNoDatesReturn()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, new DateOnly(2026, 2, 1));

        var dates = rule.OccurrencesBetween(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));

        Assert.Empty(dates);
    }

    [UnitFact]
    public void GivenOccurrenceDate_WhenMarkedMaterialized_ThenMarkerAdvances()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 31));

        rule.MarkMaterializedThrough(new DateOnly(2026, 2, 28), Now.AddDays(1));
        rule.MarkMaterializedThrough(new DateOnly(2026, 1, 31), Now.AddDays(2));

        Assert.Equal(new DateOnly(2026, 2, 28), rule.LastMaterializedOn);
    }

    [UnitFact]
    public void GivenNonOccurrenceDate_WhenMarkedMaterialized_ThenItIsRejected()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 31));

        Assert.Throws<ArgumentException>(() =>
            rule.MarkMaterializedThrough(new DateOnly(2026, 2, 27), Now));
    }

    [UnitTheory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GivenRuleEndState_WhenCompletionChecked_ThenExpectedStateReturns(
        bool bounded,
        bool expected)
    {
        var rule = Rule(
            RecurrenceFrequency.Monthly,
            new DateOnly(2026, 1, 1),
            bounded ? new DateOnly(2026, 2, 1) : null);

        Assert.Equal(expected, rule.IsCompleteOn(new DateOnly(2026, 2, 1)));
    }

    [UnitFact]
    public void GivenMatchingTransaction_WhenMarkedAsOccurrence_ThenProvenanceIsStored()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var transaction = new FinancialTransaction(
            rule.User,
            rule.FinancialAccount!,
            rule.Category,
            rule.Direction,
            rule.Amount,
            rule.StartsOn,
            Now);

        transaction.MarkAsRecurringOccurrence(rule, true, Now.AddMinutes(1));

        Assert.Same(rule, transaction.RecurringTransaction);
        Assert.True(transaction.IsPossibleDuplicate);
    }

    [UnitFact]
    public void GivenMismatchedTransaction_WhenMarkedAsOccurrence_ThenItIsRejected()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var transaction = new FinancialTransaction(
            rule.User,
            rule.FinancialAccount!,
            rule.Category,
            rule.Direction,
            rule.Amount + 1m,
            rule.StartsOn,
            Now);

        Assert.Throws<ArgumentException>(() =>
            transaction.MarkAsRecurringOccurrence(rule, false, Now));
    }

    [UnitFact]
    public void GivenAlreadyLinkedTransaction_WhenMarkedAsOccurrence_ThenItIsRejected()
    {
        var rule = Rule(RecurrenceFrequency.Monthly, new DateOnly(2026, 1, 1));
        var transaction = new FinancialTransaction(
            rule.User,
            rule.FinancialAccount!,
            rule.Category,
            rule.Direction,
            rule.Amount,
            rule.StartsOn,
            Now);
        transaction.MarkAsRecurringOccurrence(rule, false, Now);

        Assert.Throws<InvalidOperationException>(() =>
            transaction.MarkAsRecurringOccurrence(rule, false, Now));
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
