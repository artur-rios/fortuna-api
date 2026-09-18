using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PluggySynchronizationJobHandler(
    IPluggySynchronizationStore synchronizations,
    IPluggySynchronizationGateway pluggy,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => PluggySynchronizationJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<PluggySynchronizationJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        var context = await synchronizations.BeginAsync(
            request.ImportJobId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (context is null)
        {
            return ProcessOutput.New.WithError(ImportJobMessages.NotFound);
        }

        try
        {
            // The gateway authenticates with the application credentials; the key stored with the
            // connection expires after two hours and is not used for synchronization.
            var result = await pluggy.FetchAsync(
                context.ExternalReference,
                context.PeriodStart,
                context.PeriodEnd,
                cancellationToken);
            switch (result.Outcome)
            {
                case PluggySynchronizationFetchOutcome.RequiresReauthentication:
                    return await FailAsync(
                        request.ImportJobId,
                        PluggySynchronizationMessages.ReauthenticationRequired,
                        requiresReauthentication: true);
                case PluggySynchronizationFetchOutcome.ItemNotFound:
                    return await FailAsync(request.ImportJobId, PluggySynchronizationMessages.ItemNotFound);
                case PluggySynchronizationFetchOutcome.Unavailable:
                    return await FailAsync(request.ImportJobId, PluggySynchronizationMessages.SourceUnavailable);
            }

            var completion = await synchronizations.CompleteAsync(
                request.ImportJobId,
                result.Batch!,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return completion.Outcome switch
            {
                ImportCompletionOutcome.Completed => ProcessOutput.New,
                ImportCompletionOutcome.JobNotFound => ProcessOutput.New.WithError(ImportJobMessages.NotFound),
                ImportCompletionOutcome.JobNotRunning =>
                    ProcessOutput.New.WithError(ImportJobMessages.NoLongerRunning),
                _ => ProcessOutput.New.WithError(completion.Reason ?? ImportJobMessages.ProcessingFailed)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            // Unexpected (for example a database failure): leave the import job failed rather than
            // running forever, then let the processor record the defect.
            await FailAsync(request.ImportJobId, PluggySynchronizationMessages.SourceUnavailable);
            throw;
        }
    }

    private async Task<ProcessOutput> FailAsync(
        Guid importJobId,
        string reason,
        bool requiresReauthentication = false)
    {
        // The outcome is decided; host shutdown must not leave the import job running.
        await synchronizations.FailAsync(
            importJobId,
            reason,
            requiresReauthentication,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return ProcessOutput.New.WithError(reason);
    }
}
