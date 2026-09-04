using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class SynchronizeExchangeRatesCommandHandler(
    IBackgroundJobStore jobs,
    IBackgroundJobQueue queue,
    RateSyncOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<SynchronizeExchangeRatesCommand, SynchronizeExchangeRatesCommandOutput>
{
    public async Task<DataOutput<SynchronizeExchangeRatesCommandOutput?>> HandleAsync(
        SynchronizeExchangeRatesCommand command)
    {
        if (!options.IsConfigured)
        {
            return DataOutput<SynchronizeExchangeRatesCommandOutput?>.New
                .WithError(ExchangeRateSyncMessages.SourceNotConfigured);
        }

        var requestedDate = command.RequestedDate ??
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var payload = JsonSerializer.Serialize(new ExchangeRateSyncJobPayload(requestedDate));
        var job = await jobs.CreateAsync(
            ExchangeRateSyncJob.Type,
            payload,
            $"manual:{ExchangeRateSyncJob.Type}:{Guid.NewGuid():N}",
            command.CorrelationId,
            CancellationToken.None);
        await queue.EnqueueAsync(job.Id, CancellationToken.None);

        return DataOutput<SynchronizeExchangeRatesCommandOutput?>.New
            .WithData(new SynchronizeExchangeRatesCommandOutput
            {
                JobId = job.Id,
                RequestedDate = requestedDate
            })
            .WithMessage(ExchangeRateSyncMessages.Accepted);
    }
}
