namespace ArturRios.Fortuna.Integration.Rates;

public interface IRateLimitDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class RateLimitDelay(TimeProvider timeProvider) : IRateLimitDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
