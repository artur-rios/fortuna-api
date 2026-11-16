namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// A clock whose timers fire when a test moves it, so a bound of seconds costs no seconds: a
/// <see cref="CancellationTokenSource" /> or <c>Task.Delay</c> made on it ends when <see cref="Advance" /> passes
/// its due time, and not before.
/// </summary>
internal sealed class TimerClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, () => callback(state), _now + dueTime);
            _timers.Add(timer);

            return timer;
        }
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;

        lock (_gate)
        {
            _now += by;
            due = _timers.Where(timer => timer.DueAt <= _now).ToList();
            _timers.RemoveAll(due.Contains);
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(TimerClock clock, Action fire, DateTimeOffset dueAt) : ITimer
    {
        public DateTimeOffset DueAt { get; } = dueAt;

        public void Fire() => fire();

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => clock.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
