namespace ArturRios.Fortuna.Shared.Jobs;

public sealed class BackgroundJobProcessor(
    IBackgroundJobStore store,
    IEnumerable<IBackgroundJobHandler> handlers,
    TimeProvider timeProvider)
{
    private readonly IReadOnlyDictionary<string, IBackgroundJobHandler> handlers = handlers.ToDictionary(
        handler => handler.JobType,
        StringComparer.OrdinalIgnoreCase);

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Start(timeProvider.GetUtcNow());
        await store.SaveAsync(job, cancellationToken);

        try
        {
            if (!handlers.TryGetValue(job.Type, out var handler))
            {
                throw new InvalidOperationException($"No handler is registered for job type '{job.Type}'.");
            }

            await handler.ExecuteAsync(job.Payload, cancellationToken);
            job.Succeed(timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Requeue();
            throw;
        }
        catch (Exception exception)
        {
            job.Fail(exception.Message, timeProvider.GetUtcNow());
        }

        await store.SaveAsync(job, cancellationToken);
    }
}
