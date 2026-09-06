using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RetryImportJobCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid JobId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [UnitFact]
    public async Task GivenFailedOwnedJob_WhenRetried_ThenDurableAttemptIsQueued()
    {
        var store = new StubStore(Result(RetryImportJobOutcome.Succeeded));
        var queue = new StubQueue();

        var result = await Handler(store, queue).HandleAsync(
            new RetryImportJobCommand { Id = JobId });

        Assert.True(result.Success);
        Assert.Equal(JobId, result.Data?.Id);
        Assert.Equal(ImportJobStatus.Pending, result.Data?.Status);
        Assert.Equal(store.BackgroundJobId, queue.JobId);
        Assert.Equal(UserId, store.UserId);
        Assert.Equal(JobId, store.ImportJobId);
        Assert.Contains(ImportJobMessages.RetryAccepted, result.Messages);
    }

    [UnitTheory]
    [InlineData(RetryImportJobOutcome.NotFound, ImportJobMessages.NotFound)]
    [InlineData(RetryImportJobOutcome.NotFailed, ImportJobMessages.RetryRequiresFailedJob)]
    [InlineData(RetryImportJobOutcome.SourceFileNotRetained,
        ImportJobMessages.SourceFileNotRetained)]
    public async Task GivenRetryRefused_WhenHandled_ThenExpectedErrorIsReturnedWithoutQueueing(
        RetryImportJobOutcome outcome,
        string expected)
    {
        var queue = new StubQueue();
        var result = await Handler(new StubStore(Result(outcome)), queue).HandleAsync(
            new RetryImportJobCommand { Id = JobId });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
        Assert.Null(queue.JobId);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRetried_ThenStoreIsNotCalled()
    {
        var store = new StubStore(Result(RetryImportJobOutcome.Succeeded));
        var handler = new RetryImportJobCommandHandler(
            new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
            new StubProfileReader(null),
            store,
            new StubQueue(),
            new FixedTimeProvider());

        var result = await handler.HandleAsync(new RetryImportJobCommand { Id = JobId });

        Assert.False(result.Success);
        Assert.Contains(ImportJobMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.ImportJobId);
    }

    private static RetryImportJobCommandHandler Handler(StubStore store, StubQueue queue) => new(
        new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
        new StubProfileReader(new UserProfileSnapshot(
            UserId, null, "Owner", "BRL", false, Now, Now)),
        store,
        queue,
        new FixedTimeProvider());

    private static RetryImportJobResult Result(RetryImportJobOutcome outcome)
    {
        var hasJob = outcome != RetryImportJobOutcome.NotFound;
        return new RetryImportJobResult(
            outcome,
            hasJob ? new RetryImportJobSnapshot(
                JobId,
                TransactionSourceType.Excel,
                outcome == RetryImportJobOutcome.Succeeded
                    ? ImportJobStatus.Pending
                    : ImportJobStatus.Failed,
                null,
                null,
                0,
                0,
                0,
                Now.AddDays(-1),
                Now) : null,
            outcome == RetryImportJobOutcome.Succeeded ? Guid.NewGuid() : null);
    }

    private sealed class StubStore(RetryImportJobResult result) : IImportJobRetryStore
    {
        public Guid BackgroundJobId => result.BackgroundJobId!.Value;
        public Guid? UserId { get; private set; }
        public Guid? ImportJobId { get; private set; }

        public Task<RetryImportJobResult> RetryAsync(
            Guid userId,
            Guid importJobId,
            DateTimeOffset retriedAt,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            ImportJobId = importJobId;
            return Task.FromResult(result);
        }
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
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
