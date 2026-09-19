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
            services.Add(await CheckSafelyAsync(check, cancellationToken));
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

    // One failing probe must not take the whole report down: it is reported as
    // unhealthy and the remaining checks still run.
    private static async Task<OperationalHealthCheckResult> CheckSafelyAsync(
        IOperationalHealthCheck check,
        CancellationToken cancellationToken)
    {
        try
        {
            return await check.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            return new OperationalHealthCheckResult(
                check.Name,
                OperationalHealthStatus.Unhealthy,
                check.IsRequired);
        }
    }
}

public sealed record OperationalHealthOptions(TimeSpan MaximumPendingJobAge);

public interface IBackgroundJobHealthReader
{
    Task<DateTimeOffset?> GetOldestPendingAtAsync(CancellationToken cancellationToken);
}
