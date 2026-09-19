using ArturRios.Fortuna.Shared.Jobs;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class BackgroundJobHostedService(
    IBackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    /// <summary>How many times a job whose processing infrastructure failed is put back on the queue.</summary>
    public const int MaximumProcessingAttempts = 3;

    public static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(60);

    private readonly Dictionary<Guid, int> failedAttempts = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await RecoverWithRetryAsync(stoppingToken))
        {
            return;
        }

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ProcessAsync(jobId, stoppingToken);
                failedAttempts.Remove(jobId);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A store or database failure must not stop the host: log, back off, keep serving.
                consecutiveFailures++;
                var attempts = failedAttempts.GetValueOrDefault(jobId) + 1;
                logger.LogError(
                    exception,
                    "Background job {JobId} could not be processed (attempt {Attempt} of {MaximumAttempts})",
                    jobId,
                    attempts,
                    MaximumProcessingAttempts);
                if (!await BackOffAsync(consecutiveFailures, stoppingToken))
                {
                    break;
                }

                Requeue(jobId, attempts, stoppingToken);
            }
        }
    }

    public static TimeSpan Backoff(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 16);
        var delay = TimeSpan.FromTicks(InitialBackoff.Ticks * (1L << exponent));

        return delay < MaximumBackoff ? delay : MaximumBackoff;
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        logger.LogInformation("Starting background job {JobId}", jobId);
        await scope.ServiceProvider.GetRequiredService<BackgroundJobProcessor>().ProcessAsync(jobId, stoppingToken);
        logger.LogInformation("Completed background job {JobId}", jobId);
    }

    private void Requeue(Guid jobId, int attempts, CancellationToken stoppingToken)
    {
        if (attempts >= MaximumProcessingAttempts)
        {
            // The job stays pending or running in the store; startup recovery picks it up again.
            failedAttempts.Remove(jobId);
            logger.LogError("Background job {JobId} was set aside until the next restart", jobId);

            return;
        }

        failedAttempts[jobId] = attempts;
        EnqueueInBackground([jobId], stoppingToken);
    }

    /// <summary>
    /// Writes to the bounded queue without blocking this loop, which is the queue's only reader:
    /// awaiting a full queue here would never complete.
    /// </summary>
    private void EnqueueInBackground(IReadOnlyCollection<Guid> jobIds, CancellationToken stoppingToken) =>
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var jobId in jobIds)
                {
                    await queue.EnqueueAsync(jobId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down; startup recovery requeues whatever is still pending.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background jobs could not be put back on the queue");
            }
        }, CancellationToken.None);

    private async Task<bool> RecoverWithRetryAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await RecoverAsync(stoppingToken);

                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durable background job recovery failed (attempt {Attempt})", attempt);
                if (!await BackOffAsync(attempt, stoppingToken))
                {
                    break;
                }
            }
        }

        return false;
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IBackgroundJobStore>();
        var jobs = await store.RecoverAsync(cancellationToken);
        EnqueueInBackground(jobs.Select(job => job.Id).ToArray(), cancellationToken);
        logger.LogInformation("Recovered {JobCount} durable background jobs", jobs.Count);
    }

    private async Task<bool> BackOffAsync(int consecutiveFailures, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Backoff(consecutiveFailures), timeProvider, stoppingToken);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
