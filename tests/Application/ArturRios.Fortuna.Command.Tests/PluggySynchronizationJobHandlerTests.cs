using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class PluggySynchronizationJobHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenSuccessfulFetch_WhenJobRuns_ThenBatchIsCompleted()
    {
        var store = new StubStore();
        var batch = new PluggySynchronizationBatch([], []);
        var handler = Handler(
            store,
            new StubGateway(new(PluggySynchronizationFetchOutcome.Succeeded, batch)));

        var result = await handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(batch, store.CompletedBatch);
        Assert.Null(store.FailureReason);
    }

    [UnitTheory]
    [InlineData(PluggySynchronizationFetchOutcome.RequiresReauthentication, true,
        PluggySynchronizationMessages.ReauthenticationRequired)]
    [InlineData(PluggySynchronizationFetchOutcome.Unavailable, false,
        PluggySynchronizationMessages.SourceUnavailable)]
    [InlineData(PluggySynchronizationFetchOutcome.ItemNotFound, false,
        PluggySynchronizationMessages.ItemNotFound)]
    public async Task GivenFetchFailure_WhenJobRuns_ThenFailureStateIsPersisted(
        PluggySynchronizationFetchOutcome outcome,
        bool reauthentication,
        string expectedReason)
    {
        var store = new StubStore();
        var handler = Handler(store, new StubGateway(new(outcome)));

        var result = await handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None);

        Assert.Equal([expectedReason], result.Errors);
        Assert.Equal(expectedReason, store.FailureReason);
        Assert.Equal(reauthentication, store.RequiresReauthentication);
    }

    [UnitFact]
    public async Task GivenConnectionRevokedDuringFetch_WhenCompleting_ThenStoreReasonIsReturned()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Stopped(ConnectionMessages.SynchronizationStoppedByRevocation)
        };
        var handler = Handler(
            store,
            new StubGateway(new(PluggySynchronizationFetchOutcome.Succeeded, new PluggySynchronizationBatch([], []))));

        var result = await handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None);

        Assert.Equal([ConnectionMessages.SynchronizationStoppedByRevocation], result.Errors);
        Assert.Null(store.FailureReason);
    }

    [UnitFact]
    public async Task GivenStoreFailure_WhenCompleting_ThenImportJobIsFailedAndFailurePropagates()
    {
        var store = new StubStore { CompleteFailure = new IOException("database unavailable") };
        var handler = Handler(
            store,
            new StubGateway(new(PluggySynchronizationFetchOutcome.Succeeded, new PluggySynchronizationBatch([], []))));

        await Assert.ThrowsAsync<IOException>(() =>
            handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None));

        Assert.Equal(PluggySynchronizationMessages.SourceUnavailable, store.FailureReason);
        Assert.False(store.RequiresReauthentication);
    }

    private static PluggySynchronizationJobHandler Handler(
        StubStore store,
        StubGateway gateway) => new(
        store,
        gateway,
        new FixedTimeProvider(Now));

    private static string Payload(Guid jobId) => JsonSerializer.Serialize(
        new PluggySynchronizationJobPayload(jobId));

    private sealed class StubStore : IPluggySynchronizationStore
    {
        public ImportCompletionResult CompletionResult { get; init; } =
            ImportCompletionResult.Completed;
        public Exception? CompleteFailure { get; init; }
        public Guid JobId { get; } = Guid.NewGuid();
        public PluggySynchronizationBatch? CompletedBatch { get; private set; }
        public string? FailureReason { get; private set; }
        public bool RequiresReauthentication { get; private set; }

        public Task<QueueSynchronizationResult> QueueAsync(
            Guid userId, Guid connectionId, DateOnly? periodStart, DateOnly? periodEnd,
            string? correlationId, DateTimeOffset createdAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PluggySynchronizationContext?> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            Task.FromResult<PluggySynchronizationContext?>(new(
                JobId, Guid.NewGuid(), "item-1", null, null));

        public Task<ImportCompletionResult> CompleteAsync(
            Guid importJobId,
            PluggySynchronizationBatch batch,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            if (CompleteFailure is not null)
            {
                return Task.FromException<ImportCompletionResult>(CompleteFailure);
            }

            CompletedBatch = batch;

            return Task.FromResult(CompletionResult);
        }

        public Task<JobTransitionOutcome> FailAsync(
            Guid importJobId,
            string reason,
            bool requiresReauthentication,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailureReason = reason;
            RequiresReauthentication = requiresReauthentication;

            return Task.FromResult(JobTransitionOutcome.Applied);
        }
    }

    private sealed class StubGateway(PluggySynchronizationFetchResult result)
        : IPluggySynchronizationGateway
    {
        public Task<PluggySynchronizationFetchResult> FetchAsync(
            string externalReference, DateOnly? periodStart,
            DateOnly? periodEnd, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
