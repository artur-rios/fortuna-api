namespace ArturRios.Fortuna.Shared.Health;

public enum OperationalHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    NotConfigured
}

public sealed record OperationalHealthCheckResult(
    string Name,
    OperationalHealthStatus Status,
    bool IsRequired,
    int? QueueDepth = null,
    long? OldestPendingSeconds = null);

public sealed record OperationalHealthReport(
    OperationalHealthStatus Status,
    IReadOnlyCollection<OperationalHealthCheckResult> Services);

public interface IOperationalHealthCheck
{
    string Name { get; }
    bool IsRequired { get; }
    Task<OperationalHealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class OperationalHealthEvaluator(IEnumerable<IOperationalHealthCheck> checks)
{
    public async Task<OperationalHealthReport> EvaluateAsync(
        CancellationToken cancellationToken)
    {
        var services = new List<OperationalHealthCheckResult>();
        foreach (var check in checks)
        {
            services.Add(await check.CheckAsync(cancellationToken));
        }

        var requiredDown = services.Any(service =>
            service.IsRequired && service.Status != OperationalHealthStatus.Healthy);
        var optionalDown = services.Any(service =>
            !service.IsRequired && service.Status is
                OperationalHealthStatus.Degraded or OperationalHealthStatus.Unhealthy);
        var status = requiredDown
            ? OperationalHealthStatus.Unhealthy
            : optionalDown
                ? OperationalHealthStatus.Degraded
                : OperationalHealthStatus.Healthy;
        return new OperationalHealthReport(status, services);
    }
}

public sealed record OperationalHealthOptions(TimeSpan MaximumPendingJobAge);

public interface IBackgroundJobHealthReader
{
    Task<DateTimeOffset?> GetOldestPendingAtAsync(CancellationToken cancellationToken);
}
