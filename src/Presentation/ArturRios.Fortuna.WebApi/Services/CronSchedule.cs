using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ArturRios.Fortuna.WebApi.Services;

/// <summary>
/// A five-field UTC cron expression (minute, hour, day of month, month, day of week) with the
/// classic Vixie cron semantics: lists, ranges, <c>*</c>, <c>*/n</c>, <c>a-b/n</c> and <c>a/n</c>
/// (every n from a to the field maximum), 0 or 7 for Sunday, and day-of-month OR day-of-week
/// when both day fields are restricted.
/// </summary>
public sealed class CronSchedule
{
    private readonly CronField minute;
    private readonly CronField hour;
    private readonly CronField dayOfMonth;
    private readonly CronField month;
    private readonly CronField dayOfWeek;

    private CronSchedule(
        CronField minute,
        CronField hour,
        CronField dayOfMonth,
        CronField month,
        CronField dayOfWeek)
    {
        this.minute = minute;
        this.hour = hour;
        this.dayOfMonth = dayOfMonth;
        this.month = month;
        this.dayOfWeek = dayOfWeek;
    }

    public static bool TryParse(
        string? expression,
        [NotNullWhen(true)] out CronSchedule? schedule,
        [NotNullWhen(false)] out string? error)
    {
        schedule = null;
        var fields = (expression ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            error = "A cron expression must contain five fields.";

            return false;
        }

        if (!CronField.TryParse(fields[0], "minute", 0, 59, false, out var minute, out error) ||
            !CronField.TryParse(fields[1], "hour", 0, 23, false, out var hour, out error) ||
            !CronField.TryParse(fields[2], "day-of-month", 1, 31, false, out var dayOfMonth, out error) ||
            !CronField.TryParse(fields[3], "month", 1, 12, false, out var month, out error) ||
            !CronField.TryParse(fields[4], "day-of-week", 0, 7, true, out var dayOfWeek, out error))
        {
            return false;
        }

        schedule = new CronSchedule(minute, hour, dayOfMonth, month, dayOfWeek);

        return true;
    }

    public bool Matches(DateTimeOffset instant)
    {
        var date = instant.UtcDateTime;
        var domMatches = dayOfMonth.Contains(date.Day);
        var dowMatches = dayOfWeek.Contains((int)date.DayOfWeek);

        // Vixie cron: when either day field starts with '*' (including "*/n") both must match;
        // only when both are explicitly restricted does either one suffice.
        var dayMatches = dayOfMonth.StartsWithStar || dayOfWeek.StartsWithStar
            ? domMatches && dowMatches
            : domMatches || dowMatches;

        return minute.Contains(date.Minute) &&
            hour.Contains(date.Hour) &&
            month.Contains(date.Month) &&
            dayMatches;
    }

    /// <summary>
    /// Lists the matching whole minutes after <paramref name="after"/> up to and including
    /// <paramref name="until"/>, looking back at most <paramref name="maximumWindow"/>. A scheduler
    /// that was held up (for example by a full job queue) uses this to catch up on the minutes it
    /// slept through instead of silently skipping them.
    /// </summary>
    public IEnumerable<DateTimeOffset> OccurrencesBetween(
        DateTimeOffset after,
        DateTimeOffset until,
        TimeSpan maximumWindow)
    {
        var last = TruncateToMinute(until);
        var first = TruncateToMinute(after).AddMinutes(1);
        var earliest = last - maximumWindow;
        if (first < earliest)
        {
            first = TruncateToMinute(earliest);
        }

        for (var candidate = first; candidate <= last; candidate = candidate.AddMinutes(1))
        {
            if (Matches(candidate))
            {
                yield return candidate;
            }
        }
    }

    public static DateTimeOffset TruncateToMinute(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();

        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    private sealed class CronField(HashSet<int> values, bool startsWithStar)
    {
        public bool StartsWithStar { get; } = startsWithStar;

        public bool Contains(int value) => values.Contains(value);

        public static bool TryParse(
            string text,
            string name,
            int minimum,
            int maximum,
            bool normalizeSunday,
            [NotNullWhen(true)] out CronField? field,
            [NotNullWhen(false)] out string? error)
        {
            field = null;
            var values = new HashSet<int>();
            foreach (var part in text.Split(','))
            {
                if (!TryAddPart(part, minimum, maximum, normalizeSunday, values))
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The {name} field '{text}' is invalid; values must be within {minimum}-{maximum}.");

                    return false;
                }
            }

            field = new CronField(values, text.StartsWith('*'));
            error = null;

            return true;
        }

        private static bool TryAddPart(
            string part,
            int minimum,
            int maximum,
            bool normalizeSunday,
            HashSet<int> values)
        {
            var stepParts = part.Split('/');
            if (stepParts.Length > 2 || stepParts[0].Length == 0)
            {
                return false;
            }

            var increment = 1;
            if (stepParts.Length == 2 && (!TryNumber(stepParts[1], out increment) || increment <= 0))
            {
                return false;
            }

            var range = stepParts[0];
            int start;
            int end;
            if (range == "*")
            {
                start = minimum;
                end = maximum;
            }
            else if (range.Contains('-'))
            {
                var boundaries = range.Split('-');
                if (boundaries.Length != 2 ||
                    !TryNumber(boundaries[0], out start) ||
                    !TryNumber(boundaries[1], out end))
                {
                    return false;
                }
            }
            else if (TryNumber(range, out start))
            {
                // "a/n" means every n starting at a, up to the field maximum.
                end = stepParts.Length == 2 ? maximum : start;
            }
            else
            {
                return false;
            }

            if (start < minimum || end > maximum || start > end)
            {
                return false;
            }

            for (var value = start; value <= end; value += increment)
            {
                values.Add(normalizeSunday && value == 7 ? 0 : value);
            }

            return true;
        }

        private static bool TryNumber(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
