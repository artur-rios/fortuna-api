using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class SynchronizeExchangeRatesCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");

    [UnitFact]
    public async Task GivenConfiguredSource_WhenSynchronizationIsRequested_ThenDurableJobIsQueued()
    {
        var store = new StubJobStore();
        var queue = new StubQueue();
        var handler = Handler(store, queue, configured: true);

        var result = await handler.HandleAsync(new SynchronizeExchangeRatesCommand
        {
            RequestedDate = new DateOnly(2026, 9, 1),
            CorrelationId = "request-42"
        });

        Assert.True(result.Success);
        Assert.Equal(store.Created!.Id, result.Data!.JobId);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Data.RequestedDate);
        Assert.Equal(ExchangeRateSyncJob.Type, store.Created.Type);
        Assert.Equal("request-42", store.Created.CorrelationId);
        Assert.Equal(store.Created.Id, queue.JobId);
        var payload = JsonSerializer.Deserialize<ExchangeRateSyncJobPayload>(store.Created.Payload);
        Assert.Equal(new DateOnly(2026, 9, 1), payload!.RequestedDate);
    }

    [UnitFact]
    public async Task GivenDateOmitted_WhenSynchronizationIsRequested_ThenUtcTodayIsUsed()
    {
        var store = new StubJobStore();

        var result = await Handler(store, new StubQueue(), configured: true)
            .HandleAsync(new SynchronizeExchangeRatesCommand());

        Assert.Equal(new DateOnly(2026, 9, 4), result.Data!.RequestedDate);
    }

    [UnitFact]
    public async Task GivenSourceNotConfigured_WhenSynchronizationIsRequested_ThenNoJobIsCreated()
    {
        var store = new StubJobStore();
        var queue = new StubQueue();

        var result = await Handler(store, queue, configured: false)
            .HandleAsync(new SynchronizeExchangeRatesCommand());

        Assert.False(result.Success);
        Assert.Contains(ExchangeRateSyncMessages.SourceNotConfigured, result.Errors);
        Assert.Null(store.Created);
        Assert.Null(queue.JobId);
    }

    private static SynchronizeExchangeRatesCommandHandler Handler(
        StubJobStore store,
        StubQueue queue,
        bool configured) => new(
            store,
            queue,
            new RateSyncOptions(
                configured ? new Uri("https://rates.example.test/") : null,
                configured ? "0 18 * * 1-5" : null,
                configured ? ["BRL", "USD"] : []),
            new FixedTimeProvider(Now));

    private sealed class StubJobStore : IBackgroundJobStore
    {
        public BackgroundJob? Created { get; private set; }

        public Task<BackgroundJob> CreateAsync(
            string type,
            string payload,
            string idempotencyKey,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            Created = BackgroundJob.Create(type, payload, idempotencyKey, correlationId, Now);
            return Task.FromResult(Created);
        }

        public Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BackgroundJob?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(BackgroundJob job, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubQueue : IBackgroundJobQueue
    {
        public int Depth => JobId.HasValue ? 1 : 0;
        public Guid? JobId { get; private set; }

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
