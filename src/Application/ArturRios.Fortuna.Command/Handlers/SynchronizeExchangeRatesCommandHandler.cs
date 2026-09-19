using System.Globalization;
using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class SynchronizeExchangeRatesCommandHandler(
    IBackgroundJobStore jobs,
    IBackgroundJobQueue queue,
    RateSyncOptions options,
    TimeProvider timeProvider,
    IRequestActorAccessor actorAccessor)
    : ICommandHandlerAsync<SynchronizeExchangeRatesCommand, SynchronizeExchangeRatesCommandOutput>
{
    public async Task<DataOutput<SynchronizeExchangeRatesCommandOutput?>> HandleAsync(
        SynchronizeExchangeRatesCommand command)
    {
        var output = DataOutput<SynchronizeExchangeRatesCommandOutput?>.New;
        if (!InstallationAdministration.IsAdministrator(actorAccessor.Actor))
        {
            return output.WithError(ExchangeRateSyncMessages.AdministratorRequired);
        }

        if (!options.IsConfigured)
        {
            return output.WithError(ExchangeRateSyncMessages.SourceNotConfigured);
        }

        var requestedDate = command.RequestedDate ??
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Repeated requests for the same date reuse the job that is still queued or running
        // instead of piling identical work onto the bounded job queue.
        var keyPrefix = string.Create(
            CultureInfo.InvariantCulture,
            $"manual:{ExchangeRateSyncJob.Type}:{requestedDate:yyyyMMdd}:");
        var active = await jobs.FindActiveAsync(
            ExchangeRateSyncJob.Type,
            keyPrefix,
            CancellationToken.None);
        if (active is not null)
        {
            return output
                .WithData(new SynchronizeExchangeRatesCommandOutput
                {
                    JobId = active.Id,
                    RequestedDate = requestedDate
                })
                .WithMessage(ExchangeRateSyncMessages.AlreadyQueued);
        }

        var payload = JsonSerializer.Serialize(new ExchangeRateSyncJobPayload(requestedDate));
        var job = await jobs.CreateAsync(
            ExchangeRateSyncJob.Type,
            payload,
            $"{keyPrefix}{Guid.NewGuid():N}",
            command.CorrelationId,
            CancellationToken.None);
        await queue.EnqueueAsync(job.Id, CancellationToken.None);

        return output
            .WithData(new SynchronizeExchangeRatesCommandOutput
            {
                JobId = job.Id,
                RequestedDate = requestedDate
            })
            .WithMessage(ExchangeRateSyncMessages.Accepted);
    }
}
