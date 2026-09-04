using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class BackgroundJobQueueTests
{
    [UnitFact]
    public void GivenNonPositiveCapacity_WhenQueueIsCreated_ThenArgumentOutOfRangeExceptionIsThrown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackgroundJobQueue(0));
    }

    [UnitFact]
    public async Task GivenQueuedJob_WhenDequeued_ThenTheSameIdentifierIsReturned()
    {
        var queue = new BackgroundJobQueue(2);
        var id = Guid.NewGuid();

        await queue.EnqueueAsync(id, CancellationToken.None);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(id, dequeued);
        Assert.Equal(0, queue.Depth);
    }

    [UnitFact]
    public async Task GivenFullQueue_WhenAnotherJobIsQueued_ThenTheWriterWaitsForCapacity()
    {
        var queue = new BackgroundJobQueue(1);
        await queue.EnqueueAsync(Guid.NewGuid(), CancellationToken.None);

        var pendingWrite = queue.EnqueueAsync(Guid.NewGuid(), CancellationToken.None).AsTask();

        Assert.False(pendingWrite.IsCompleted);
        await queue.DequeueAsync(CancellationToken.None);
        await pendingWrite;
    }
}

public sealed class BackgroundJobProcessorTests
{
    [UnitFact]
    public async Task GivenMatchingHandler_WhenJobIsProcessed_ThenPayloadExecutesAndJobSucceeds()
    {
        var job = BackgroundJob.Create("import", "{\"row\":1}", "request", null, DateTimeOffset.UtcNow);
        var store = new StubStore(job);
        var handler = new StubHandler();
        var processor = new BackgroundJobProcessor(store, [handler], TimeProvider.System);

        await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal("{\"row\":1}", handler.Payload);
        Assert.Equal(BackgroundJobState.Succeeded, job.State);
    }

    [UnitFact]
    public async Task GivenMissingJob_WhenProcessed_ThenNothingIsSaved()
    {
        var store = new StubStore(null);
        var processor = new BackgroundJobProcessor(store, [], TimeProvider.System);

        await processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, store.SaveCount);
    }

    [UnitFact]
    public async Task GivenNoMatchingHandler_WhenProcessed_ThenJobFailsWithActionableReason()
    {
        var job = BackgroundJob.Create("missing", "{}", "request", null, DateTimeOffset.UtcNow);
        var store = new StubStore(job);
        var processor = new BackgroundJobProcessor(store, [], TimeProvider.System);

        await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BackgroundJobState.Failed, job.State);
        Assert.Contains("No handler is registered", job.FailureReason, StringComparison.Ordinal);
        Assert.Equal(2, store.SaveCount);
    }

    [UnitFact]
    public async Task GivenHandlerThrows_WhenProcessed_ThenJobFailsAndErrorIsPersisted()
    {
        var job = BackgroundJob.Create("import", "{}", "request", null, DateTimeOffset.UtcNow);
        var store = new StubStore(job);
        var processor = new BackgroundJobProcessor(store, [new StubHandler(new InvalidDataException("bad file"))], TimeProvider.System);

        await processor.ProcessAsync(job.Id, CancellationToken.None);

        Assert.Equal(BackgroundJobState.Failed, job.State);
        Assert.Equal("bad file", job.FailureReason);
        Assert.Equal(2, store.SaveCount);
    }

    [UnitFact]
    public async Task GivenCancellation_WhenProcessed_ThenJobIsRequeuedAndCancellationPropagates()
    {
        var job = BackgroundJob.Create("import", "{}", "request", null, DateTimeOffset.UtcNow);
        var store = new StubStore(job);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var processor = new BackgroundJobProcessor(store, [new StubHandler(new OperationCanceledException(cancellation.Token))], TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(job.Id, cancellation.Token));

        Assert.Equal(BackgroundJobState.Pending, job.State);
    }

    private sealed class StubHandler(Exception? exception = null) : IBackgroundJobHandler
    {
        public string JobType => "import";
        public string? Payload { get; private set; }

        public Task ExecuteAsync(string payload, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            Payload = payload;
            return Task.CompletedTask;
        }
    }

    private sealed class StubStore(BackgroundJob? job) : IBackgroundJobStore
    {
        public int SaveCount { get; private set; }

        public Task<BackgroundJob> CreateAsync(
            string type,
            string payload,
            string idempotencyKey,
            string? correlationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<BackgroundJob?>(job is not null && id == job.Id ? job : null);

        public Task<BackgroundJob?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackgroundJob>>([]);

        public Task SaveAsync(BackgroundJob changedJob, CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
