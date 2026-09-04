namespace ArturRios.Fortuna.WebApi.Services;

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

    public static CronSchedule Parse(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException("A rate synchronization cron must contain five fields.");
        }

        return new CronSchedule(
            CronField.Parse(fields[0], 0, 59),
            CronField.Parse(fields[1], 0, 23),
            CronField.Parse(fields[2], 1, 31),
            CronField.Parse(fields[3], 1, 12),
            CronField.Parse(fields[4], 0, 7, normalizeSunday: true));
    }

    public bool Matches(DateTimeOffset instant)
    {
        var date = instant.UtcDateTime;
        var dayMatches = dayOfMonth.IsWildcard
            ? dayOfWeek.Contains((int)date.DayOfWeek)
            : dayOfWeek.IsWildcard
                ? dayOfMonth.Contains(date.Day)
                : dayOfMonth.Contains(date.Day) || dayOfWeek.Contains((int)date.DayOfWeek);

        return minute.Contains(date.Minute) &&
            hour.Contains(date.Hour) &&
            month.Contains(date.Month) &&
            dayMatches;
    }

    private sealed class CronField(HashSet<int> values, bool isWildcard)
    {
        public bool IsWildcard { get; } = isWildcard;

        public bool Contains(int value) => values.Contains(value);

        public static CronField Parse(
            string text,
            int minimum,
            int maximum,
            bool normalizeSunday = false)
        {
            var values = new HashSet<int>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length > 2 ||
                    (stepParts.Length == 2 && (!int.TryParse(stepParts[1], out var step) || step <= 0)))
                {
                    throw new FormatException($"Invalid cron field '{text}'.");
                }

                var increment = stepParts.Length == 2 ? int.Parse(stepParts[1]) : 1;
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
                        !int.TryParse(boundaries[0], out start) ||
                        !int.TryParse(boundaries[1], out end))
                    {
                        throw new FormatException($"Invalid cron field '{text}'.");
                    }
                }
                else if (int.TryParse(range, out start))
                {
                    end = start;
                }
                else
                {
                    throw new FormatException($"Invalid cron field '{text}'.");
                }

                if (start < minimum || end > maximum || start > end)
                {
                    throw new FormatException($"Cron field '{text}' is out of range.");
                }

                for (var value = start; value <= end; value += increment)
                {
                    values.Add(normalizeSunday && value == 7 ? 0 : value);
                }
            }

            if (values.Count == 0)
            {
                throw new FormatException($"Invalid cron field '{text}'.");
            }

            return new CronField(values, text == "*");
        }
    }
}
