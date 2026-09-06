using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class SynchronizeConnectionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenActiveConnection_WhenSynchronized_ThenDurableJobIsQueued()
    {
        var store = new StubStore(QueueSynchronizationOutcome.Succeeded);
        var queue = new StubQueue();
        var command = new SynchronizeConnectionCommand
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateOnly(2026, 8, 1),
            PeriodEnd = new DateOnly(2026, 8, 31),
            CorrelationId = "request-42"
        };

        var result = await Handler(store, queue).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(store.JobId, result.Data?.ImportJobId);
        Assert.Equal(ImportJobStatus.Pending, result.Data?.Status);
        Assert.Equal(store.BackgroundJobId, queue.JobId);
        Assert.Equal(command.Id, store.ConnectionId);
        Assert.Equal(command.PeriodStart, store.PeriodStart);
        Assert.Equal(command.PeriodEnd, store.PeriodEnd);
        Assert.Equal("request-42", store.CorrelationId);
    }

    [UnitFact]
    public async Task GivenSynchronizationRunning_WhenRequested_ThenExistingJobIsReturned()
    {
        var store = new StubStore(QueueSynchronizationOutcome.AlreadyRunning);
        var queue = new StubQueue();

        var result = await Handler(store, queue).HandleAsync(new SynchronizeConnectionCommand
        {
            Id = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Equal(store.JobId, result.Data?.ImportJobId);
        Assert.Contains(PluggySynchronizationMessages.AlreadyRunning, result.Errors);
        Assert.Null(queue.JobId);
    }

    [UnitTheory]
    [InlineData(QueueSynchronizationOutcome.ConnectionNotFound,
        PluggySynchronizationMessages.ConnectionNotFound)]
    [InlineData(QueueSynchronizationOutcome.ConnectionInactive,
        PluggySynchronizationMessages.ConnectionInactive)]
    [InlineData(QueueSynchronizationOutcome.ConnectionRequiresReauthentication,
        ConnectionMessages.RequiresReauthentication)]
    [InlineData(QueueSynchronizationOutcome.ConnectionRevoked,
        ConnectionMessages.Revoked)]
    public async Task GivenUnavailableConnection_WhenRequested_ThenExpectedErrorIsReturned(
        QueueSynchronizationOutcome outcome,
        string expected)
    {
        var store = new StubStore(outcome);

        var result = await Handler(store, new StubQueue()).HandleAsync(
            new SynchronizeConnectionCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidPeriod_WhenRequested_ThenStoreIsNotCalled()
    {
        var store = new StubStore(QueueSynchronizationOutcome.Succeeded);

        var result = await Handler(store, new StubQueue()).HandleAsync(
            new SynchronizeConnectionCommand
            {
                Id = Guid.NewGuid(),
                PeriodStart = new DateOnly(2026, 9, 2),
                PeriodEnd = new DateOnly(2026, 9, 1)
            });

        Assert.False(result.Success);
        Assert.Contains(PluggySynchronizationMessages.PeriodInvalid, result.Errors);
        Assert.Equal(0, store.CallCount);
    }

    private static SynchronizeConnectionCommandHandler Handler(
        StubStore store,
        StubQueue queue) => new(
        new SynchronizeConnectionCommandValidator(),
        new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(new UserProfileSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now)),
        store,
        queue,
        new FixedTimeProvider(Now));

    private sealed class StubStore(QueueSynchronizationOutcome outcome)
        : IPluggySynchronizationStore
    {
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid BackgroundJobId { get; } = Guid.NewGuid();
        public int CallCount { get; private set; }
        public Guid? ConnectionId { get; private set; }
        public DateOnly? PeriodStart { get; private set; }
        public DateOnly? PeriodEnd { get; private set; }
        public string? CorrelationId { get; private set; }

        public Task<QueueSynchronizationResult> QueueAsync(
            Guid userId,
            Guid connectionId,
            DateOnly? periodStart,
            DateOnly? periodEnd,
            string? correlationId,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ConnectionId = connectionId;
            PeriodStart = periodStart;
            PeriodEnd = periodEnd;
            CorrelationId = correlationId;
            var job = outcome is QueueSynchronizationOutcome.Succeeded or
                QueueSynchronizationOutcome.AlreadyRunning
                ? new ImportJobSnapshot(
                    JobId, connectionId, TransactionSourceType.Pluggy,
                    ImportJobStatus.Pending, periodStart, periodEnd,
                    0, 0, 0, null, createdAt, createdAt)
                : null;
            return Task.FromResult(new QueueSynchronizationResult(
                job,
                outcome == QueueSynchronizationOutcome.Succeeded ? BackgroundJobId : null,
                outcome));
        }

        public Task<PluggySynchronizationContext?> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CompleteAsync(
            Guid importJobId, PluggySynchronizationBatch batch,
            DateTimeOffset completedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            Guid importJobId, string reason, bool requiresReauthentication,
            DateTimeOffset failedAt, CancellationToken cancellationToken) =>
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

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
