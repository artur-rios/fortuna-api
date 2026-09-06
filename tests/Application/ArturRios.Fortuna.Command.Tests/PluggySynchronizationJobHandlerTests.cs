using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Shared.Ingestion;
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

        await handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None);

        Assert.Equal(batch, store.CompletedBatch);
        Assert.Null(store.FailureReason);
    }

    [UnitTheory]
    [InlineData(PluggySynchronizationFetchOutcome.RequiresReauthentication, true,
        PluggySynchronizationMessages.ReauthenticationRequired)]
    [InlineData(PluggySynchronizationFetchOutcome.Unavailable, false,
        PluggySynchronizationMessages.SourceUnavailable)]
    public async Task GivenFetchFailure_WhenJobRuns_ThenFailureStateIsPersisted(
        PluggySynchronizationFetchOutcome outcome,
        bool reauthentication,
        string expectedReason)
    {
        var store = new StubStore();
        var handler = Handler(store, new StubGateway(new(outcome)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync(Payload(store.JobId), CancellationToken.None));

        Assert.Equal(expectedReason, exception.Message);
        Assert.Equal(expectedReason, store.FailureReason);
        Assert.Equal(reauthentication, store.RequiresReauthentication);
    }

    private static PluggySynchronizationJobHandler Handler(
        StubStore store,
        StubGateway gateway) => new(
        store,
        gateway,
        new StubProtector(),
        new FixedTimeProvider(Now));

    private static string Payload(Guid jobId) => JsonSerializer.Serialize(
        new PluggySynchronizationJobPayload(jobId));

    private sealed class StubStore : IPluggySynchronizationStore
    {
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
                JobId, Guid.NewGuid(), "item-1", [1, 2, 3], null, null));

        public Task CompleteAsync(
            Guid importJobId,
            PluggySynchronizationBatch batch,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            CompletedBatch = batch;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid importJobId,
            string reason,
            bool requiresReauthentication,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailureReason = reason;
            RequiresReauthentication = requiresReauthentication;
            return Task.CompletedTask;
        }
    }

    private sealed class StubGateway(PluggySynchronizationFetchResult result)
        : IPluggySynchronizationGateway
    {
        public Task<PluggySynchronizationFetchResult> FetchAsync(
            string externalReference, string accessToken, DateOnly? periodStart,
            DateOnly? periodEnd, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class StubProtector : IConnectionAccessTokenProtector
    {
        public byte[] Protect(string accessToken) => throw new NotSupportedException();

        public string Unprotect(byte[] protectedAccessToken) => "plain-token";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
