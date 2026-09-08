using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Jobs;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class AttachmentStorageOperationalHealthCheck(IAttachmentStore storage)
    : IOperationalHealthCheck
{
    public string Name => "AttachmentStorage";
    public bool IsRequired => true;

    public async Task<OperationalHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var healthy = await storage.IsHealthyAsync(cancellationToken);
            return Result(healthy
                ? OperationalHealthStatus.Healthy
                : OperationalHealthStatus.Unhealthy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result(OperationalHealthStatus.Unhealthy);
        }
    }

    private OperationalHealthCheckResult Result(OperationalHealthStatus status) =>
        new(Name, status, IsRequired);
}

public sealed class JobRunnerOperationalHealthCheck(
    IBackgroundJobQueue queue,
    IBackgroundJobHealthReader jobs,
    OperationalHealthOptions options,
    TimeProvider timeProvider) : IOperationalHealthCheck
{
    public string Name => "JobRunner";
    public bool IsRequired => true;

    public async Task<OperationalHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var oldest = await jobs.GetOldestPendingAtAsync(cancellationToken);
            var age = oldest.HasValue
                ? Math.Max(0L, (long)(timeProvider.GetUtcNow() - oldest.Value).TotalSeconds)
                : 0L;
            var status = age > options.MaximumPendingJobAge.TotalSeconds
                ? OperationalHealthStatus.Unhealthy
                : OperationalHealthStatus.Healthy;
            return new OperationalHealthCheckResult(
                Name,
                status,
                IsRequired,
                queue.Depth,
                age);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new OperationalHealthCheckResult(
                Name,
                OperationalHealthStatus.Unhealthy,
                IsRequired,
                queue.Depth,
                0);
        }
    }
}

public sealed class ExternalServiceOperationalHealthCheck(
    string name,
    bool configured,
    HttpClient client) : IOperationalHealthCheck
{
    public string Name { get; } = name;
    public bool IsRequired => false;

    public async Task<OperationalHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        if (!configured)
        {
            return Result(OperationalHealthStatus.NotConfigured);
        }

        try
        {
            using var response = await client.GetAsync(
                string.Empty,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return Result(OperationalHealthStatus.Healthy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result(OperationalHealthStatus.Degraded);
        }
    }

    private OperationalHealthCheckResult Result(OperationalHealthStatus status) =>
        new(Name, status, IsRequired);
}

public static class OperationalHealthClientNames
{
    public const string Aggregator = "OperationalHealthAggregator";
    public const string ExchangeRateSource = "OperationalHealthExchangeRateSource";
}
