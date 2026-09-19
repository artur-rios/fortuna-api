using System.Text.Json;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class ExchangeRateSyncHostedService(
    RateSyncOptions options,
    IServiceScopeFactory scopeFactory,
    IBackgroundJobQueue queue,
    TimeProvider timeProvider,
    ILogger<ExchangeRateSyncHostedService> logger) : BackgroundService
{
    // How far back a held-up scheduler catches up on minutes it slept through.
    private static readonly TimeSpan MaximumCatchUp = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured || string.IsNullOrWhiteSpace(options.Cron))
        {
            return;
        }

        if (!CronSchedule.TryParse(options.Cron, out var schedule, out var error))
        {
            // Startup validation rejects an invalid cron, so this only guards direct construction.
            logger.LogError("The exchange-rate synchronization schedule is invalid: {Error}", error);

            return;
        }

        var lastConsidered = CronSchedule.TruncateToMinute(timeProvider.GetUtcNow());
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextMinute = lastConsidered.AddMinutes(1);
            if (nextMinute > now)
            {
                await Task.Delay(nextMinute - now, timeProvider, stoppingToken);
            }

            // Enqueueing waits while the job queue is full, so several minutes can pass in one
            // iteration; every matching minute since the last one considered is scheduled.
            now = timeProvider.GetUtcNow();
            foreach (var occurrence in schedule.OccurrencesBetween(lastConsidered, now, MaximumCatchUp))
            {
                await TryEnqueueAsync(occurrence, stoppingToken);
            }

            lastConsidered = CronSchedule.TruncateToMinute(now);
        }
    }

    private async Task TryEnqueueAsync(DateTimeOffset now, CancellationToken stoppingToken)
    {
        try
        {
            await EnqueueAsync(now, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed tick (for example a database outage) must not stop the schedule; the next
            // matching minute tries again.
            logger.LogError(exception, "Scheduling the exchange-rate synchronization job failed");
        }
    }

    private async Task EnqueueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var idempotencyKey = $"scheduled:{ExchangeRateSyncJob.Type}:{now:yyyyMMddHHmm}";
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobStore>();
        if (await jobs.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken) is not null)
        {
            return;
        }

        var requestedDate = DateOnly.FromDateTime(now.UtcDateTime);
        var job = await jobs.CreateAsync(
            ExchangeRateSyncJob.Type,
            JsonSerializer.Serialize(new ExchangeRateSyncJobPayload(requestedDate)),
            idempotencyKey,
            null,
            cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        logger.LogInformation("Scheduled exchange-rate synchronization job");
    }
}
