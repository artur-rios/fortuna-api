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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured || string.IsNullOrWhiteSpace(options.Cron))
        {
            return;
        }

        var schedule = CronSchedule.Parse(options.Cron);
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextMinute = new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute,
                0,
                TimeSpan.Zero).AddMinutes(1);
            await Task.Delay(nextMinute - now, timeProvider, stoppingToken);
            now = timeProvider.GetUtcNow();
            if (schedule.Matches(now))
            {
                await EnqueueAsync(now, stoppingToken);
            }
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
