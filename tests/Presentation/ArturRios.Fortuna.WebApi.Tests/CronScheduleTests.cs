using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class CronScheduleTests
{
    [UnitTheory]
    [InlineData("5/15 * * * *", new[] { 5, 20, 35, 50 })]
    [InlineData("*/20 * * * *", new[] { 0, 20, 40 })]
    [InlineData("10-30/10 * * * *", new[] { 10, 20, 30 })]
    [InlineData("7,45 * * * *", new[] { 7, 45 })]
    [InlineData("58/1 * * * *", new[] { 58, 59 })]
    public void GivenMinuteExpression_WhenMatchingEachMinute_ThenExactlyTheExpectedMinutesMatch(
        string expression,
        int[] expected)
    {
        var schedule = Parse(expression);
        var hour = DateTimeOffset.Parse("2026-09-04T10:00:00Z");

        var matched = Enumerable.Range(0, 60)
            .Where(minute => schedule.Matches(hour.AddMinutes(minute)))
            .ToArray();

        Assert.Equal(expected, matched);
    }

    [UnitFact]
    public void GivenWeekdayCron_WhenMatchingUtcInstants_ThenOnlyScheduledMinutesMatch()
    {
        var schedule = Parse("*/15 9-17 * * 1-5");

        Assert.True(schedule.Matches(DateTimeOffset.Parse("2026-09-04T09:30:00Z")));
        Assert.False(schedule.Matches(DateTimeOffset.Parse("2026-09-05T09:30:00Z")));
        Assert.False(schedule.Matches(DateTimeOffset.Parse("2026-09-04T09:31:00Z")));
    }

    [UnitTheory]
    // "*/2" selects odd days. 2026-09-03 is an odd Thursday; 2026-09-02 an even Wednesday and
    // 2026-09-05 an odd Saturday, each of which only one field accepts.
    [InlineData("2026-09-03T00:00:00Z", true)]
    [InlineData("2026-09-02T00:00:00Z", false)]
    [InlineData("2026-09-05T00:00:00Z", false)]
    public void GivenStepDayOfMonthAndRestrictedWeekday_WhenMatching_ThenBothMustMatch(
        string instant,
        bool expected)
    {
        // "*/2" starts with '*', so Vixie cron intersects it with the weekday field.
        var schedule = Parse("0 0 */2 * 1-5");

        Assert.Equal(expected, schedule.Matches(DateTimeOffset.Parse(instant)));
    }

    [UnitTheory]
    // The 1st of September 2026 is a Tuesday; the 7th is a Monday; the 8th a Tuesday.
    [InlineData("2026-09-01T00:00:00Z", true)]
    [InlineData("2026-09-07T00:00:00Z", true)]
    [InlineData("2026-09-08T00:00:00Z", false)]
    public void GivenBothDayFieldsRestricted_WhenMatching_ThenEitherSuffices(string instant, bool expected)
    {
        var schedule = Parse("0 0 1 * 1");

        Assert.Equal(expected, schedule.Matches(DateTimeOffset.Parse(instant)));
    }

    [UnitFact]
    public void GivenSundayWrittenAsSeven_WhenMatching_ThenSundayMatches()
    {
        var schedule = Parse("0 12 * * 7");

        Assert.True(schedule.Matches(DateTimeOffset.Parse("2026-09-06T12:00:00Z")));
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("* * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("1,,2 * * * *")]
    [InlineData("1/2/3 * * * *")]
    [InlineData("-1 * * * *")]
    [InlineData("a-b * * * *")]
    public void GivenInvalidExpression_WhenParsed_ThenErrorIsReturnedWithoutThrowing(string expression)
    {
        var parsed = CronSchedule.TryParse(expression, out var schedule, out var error);

        Assert.False(parsed);
        Assert.Null(schedule);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [UnitFact]
    public void GivenSchedulerHeldUp_WhenListingOccurrences_ThenMissedMinutesAreReturned()
    {
        var schedule = Parse("*/5 * * * *");

        var occurrences = schedule.OccurrencesBetween(
            DateTimeOffset.Parse("2026-09-04T10:02:00Z"),
            DateTimeOffset.Parse("2026-09-04T10:16:30Z"),
            TimeSpan.FromHours(1));

        Assert.Equal(
            [
                DateTimeOffset.Parse("2026-09-04T10:05:00Z"),
                DateTimeOffset.Parse("2026-09-04T10:10:00Z"),
                DateTimeOffset.Parse("2026-09-04T10:15:00Z")
            ],
            occurrences);
    }

    [UnitFact]
    public void GivenLastMinuteAlreadyConsidered_WhenListingOccurrences_ThenItIsNotRepeated()
    {
        var schedule = Parse("* * * * *");

        var occurrences = schedule.OccurrencesBetween(
            DateTimeOffset.Parse("2026-09-04T10:05:00Z"),
            DateTimeOffset.Parse("2026-09-04T10:05:40Z"),
            TimeSpan.FromHours(1));

        Assert.Empty(occurrences);
    }

    [UnitFact]
    public void GivenGapLongerThanWindow_WhenListingOccurrences_ThenOnlyTheWindowIsCaughtUp()
    {
        var schedule = Parse("0 * * * *");

        var occurrences = schedule.OccurrencesBetween(
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-04T10:00:00Z"),
            TimeSpan.FromHours(3));

        Assert.Equal(
            [
                DateTimeOffset.Parse("2026-09-04T07:00:00Z"),
                DateTimeOffset.Parse("2026-09-04T08:00:00Z"),
                DateTimeOffset.Parse("2026-09-04T09:00:00Z"),
                DateTimeOffset.Parse("2026-09-04T10:00:00Z")
            ],
            occurrences);
    }

    private static CronSchedule Parse(string expression)
    {
        Assert.True(CronSchedule.TryParse(expression, out var schedule, out var error), error);

        return schedule;
    }
}
