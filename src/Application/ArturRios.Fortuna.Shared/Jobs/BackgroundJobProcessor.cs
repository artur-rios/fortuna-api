using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Shared.Jobs;

public sealed class BackgroundJobProcessor(
    IBackgroundJobStore store,
    IEnumerable<IBackgroundJobHandler> handlers,
    TimeProvider timeProvider,
    ILogger<BackgroundJobProcessor> logger)
{
    private readonly IReadOnlyDictionary<string, IBackgroundJobHandler> handlers = handlers.ToDictionary(
        handler => handler.JobType,
        StringComparer.OrdinalIgnoreCase);

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(jobId, cancellationToken);
        if (job is null || job.State is BackgroundJobState.Succeeded or BackgroundJobState.Failed)
        {
            return;
        }

        // A job found running was interrupted by an earlier attempt of this process; it resumes.
        if (job.State == BackgroundJobState.Pending)
        {
            job.Start(timeProvider.GetUtcNow());
            await store.SaveAsync(job, cancellationToken);
        }

        ProcessOutput output;
        try
        {
            output = handlers.TryGetValue(job.Type, out var handler)
                ? await handler.ExecuteAsync(job.Payload, cancellationToken)
                : ProcessOutput.New.WithError(BackgroundJobMessages.HandlerNotRegistered(job.Type));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Requeue();
            await TrySaveRequeueAsync(job);
            throw;
        }
        catch (Exception exception)
        {
            // Last resort: handlers report expected failures as errors, so this is a defect.
            logger.LogError(exception, "Background job {JobId} of type {JobType} threw", job.Id, job.Type);
            output = ProcessOutput.New.WithError(exception.Message);
        }

        if (output.Success)
        {
            job.Succeed(timeProvider.GetUtcNow());
        }
        else
        {
            job.Fail(string.Join(" ", output.Errors), timeProvider.GetUtcNow());
        }

        // The outcome is already decided; stopping the host must not leave it unrecorded.
        await store.SaveAsync(job, CancellationToken.None);
    }

    private async Task TrySaveRequeueAsync(BackgroundJob job)
    {
        try
        {
            await store.SaveAsync(job, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Startup recovery requeues jobs left running, so the job is not lost.
            logger.LogWarning(exception, "Could not persist the requeue of background job {JobId}", job.Id);
        }
    }
}
