using System.Text.Json;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PluggySynchronizationJobHandler(
    IPluggySynchronizationStore synchronizations,
    IPluggySynchronizationGateway pluggy,
    IConnectionAccessTokenProtector protector,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => PluggySynchronizationJob.Type;

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<PluggySynchronizationJobPayload>(payload)
            ?? throw new InvalidOperationException("The Pluggy synchronization payload is invalid.");
        var startedAt = timeProvider.GetUtcNow();
        var context = await synchronizations.BeginAsync(
            request.ImportJobId,
            startedAt,
            cancellationToken);
        if (context is null)
        {
            throw new InvalidOperationException("The Pluggy import job was not found.");
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
                throw new InvalidOperationException(reason);
            }

            await synchronizations.CompleteAsync(
                request.ImportJobId,
                result.Batch!,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception) when (
            exception.Message is PluggySynchronizationMessages.ReauthenticationRequired or
                PluggySynchronizationMessages.SourceUnavailable)
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
