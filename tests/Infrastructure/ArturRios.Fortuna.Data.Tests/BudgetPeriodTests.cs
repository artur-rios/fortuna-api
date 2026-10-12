using ArturRios.Fortuna.Data.Planning;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class BudgetPeriodTests
{
    [UnitTheory]
    [InlineData("2026-01-31", BudgetPeriodType.Monthly, "2026-03-29", "2026-02-28", "2026-03-30")]
    [InlineData("2026-01-31", BudgetPeriodType.Monthly, "2026-03-31", "2026-03-31", "2026-04-29")]
    [InlineData("2026-01-31", BudgetPeriodType.Monthly, "2026-05-30", "2026-04-30", "2026-05-30")]
    [InlineData("2026-01-31", BudgetPeriodType.Monthly, "2026-02-27", "2026-01-31", "2026-02-27")]
    [InlineData("2025-11-30", BudgetPeriodType.Quarterly, "2026-05-29", "2026-02-28", "2026-05-29")]
    [InlineData("2026-01-15", BudgetPeriodType.Monthly, "2026-03-20", "2026-03-15", "2026-04-14")]
    [InlineData("2024-02-29", BudgetPeriodType.Yearly, "2025-02-28", "2025-02-28", "2026-02-27")]
    public void GivenAnchorAndDay_WhenFindingItsPeriod_ThenThePeriodContainsTheDay(
        string anchor,
        BudgetPeriodType periodType,
        string asOf,
        string expectedStart,
        string expectedEnd)
    {
        var (start, end) = EfBudgetStore.PeriodContaining(
            DateOnly.Parse(anchor), periodType, DateOnly.Parse(asOf));

        Assert.Equal(DateOnly.Parse(expectedStart), start);
        Assert.Equal(DateOnly.Parse(expectedEnd), end);
    }

    [UnitFact]
    public void GivenMonthEndAnchor_WhenWalkingEveryDayOfAYear_ThenPeriodsAreContiguous()
    {
        var anchor = new DateOnly(2026, 1, 31);
        for (var day = anchor; day < anchor.AddYears(1); day = day.AddDays(1))
        {
            var (start, end) = EfBudgetStore.PeriodContaining(anchor, BudgetPeriodType.Monthly, day);
            Assert.InRange(day, start, end);
            var (nextStart, _) = EfBudgetStore.PeriodContaining(
                anchor, BudgetPeriodType.Monthly, end.AddDays(1));
            Assert.Equal(end.AddDays(1), nextStart);
        }
    }
}
