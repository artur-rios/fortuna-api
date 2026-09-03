using ArturRios.Fortuna.Shared.Jobs;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class BackgroundJobHostedService(
    IBackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await queue.DequeueAsync(stoppingToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            logger.LogInformation("Starting background job");
            await scope.ServiceProvider.GetRequiredService<BackgroundJobProcessor>().ProcessAsync(jobId, stoppingToken);
            logger.LogInformation("Completed background job");
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IBackgroundJobStore>();
        var jobs = await store.RecoverAsync(cancellationToken);
        foreach (var job in jobs)
        {
            await queue.EnqueueAsync(job.Id, cancellationToken);
        }

        logger.LogInformation("Recovered {JobCount} durable background jobs", jobs.Count);
    }
}
