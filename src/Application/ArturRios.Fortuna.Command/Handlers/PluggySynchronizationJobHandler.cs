using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PluggySynchronizationJobHandler(
    IPluggySynchronizationStore synchronizations,
    IPluggySynchronizationGateway pluggy,
    IConnectionAccessTokenProtector protector,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => PluggySynchronizationJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<PluggySynchronizationJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        var startedAt = timeProvider.GetUtcNow();
        var context = await synchronizations.BeginAsync(
            request.ImportJobId,
            startedAt,
            cancellationToken);
        if (context is null)
        {
            return ProcessOutput.New.WithError(ImportJobMessages.NotFound);
        }

        try
        {
            var result = await pluggy.FetchAsync(
                context.ExternalReference,
                protector.Unprotect(context.AccessTokenCipher),
                context.PeriodStart,
                context.PeriodEnd,
                cancellationToken);
            if (result.Outcome != PluggySynchronizationFetchOutcome.Succeeded)
            {
                var reauthentication = result.Outcome ==
                    PluggySynchronizationFetchOutcome.RequiresReauthentication;
                var reason = reauthentication
                    ? PluggySynchronizationMessages.ReauthenticationRequired
                    : PluggySynchronizationMessages.SourceUnavailable;
                await synchronizations.FailAsync(
                    request.ImportJobId,
                    reason,
                    reauthentication,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return ProcessOutput.New.WithError(reason);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await synchronizations.FailAsync(
                request.ImportJobId,
                PluggySynchronizationMessages.SourceUnavailable,
                false,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
