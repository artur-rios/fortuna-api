using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class TransferLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTransfer_WhenDeleted_ThenCanonicalResultIsReturned()
    {
        var profile = Profile();
        var transferId = Guid.NewGuid();
        var store = new StubLifecycleStore(new(
            transferId,
            TransferLifecycleOutcome.Succeeded));

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteTransferCommand { Id = transferId });

        Assert.True(result.Success);
        Assert.Equal(transferId, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Equal(transferId, store.TransferId);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(TransferMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedTransfer_WhenRestored_ThenCanonicalResultIsReturned()
    {
        var profile = Profile();
        var transferId = Guid.NewGuid();
        var store = new StubLifecycleStore(new(
            transferId,
            TransferLifecycleOutcome.Succeeded));

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreTransferCommand { Id = transferId });

        Assert.True(result.Success);
        Assert.Equal(transferId, result.Data?.Id);
        Assert.True(store.RestoreCalled);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(TransferMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(TransferLifecycleOutcome.NotFound, TransferMessages.NotFound)]
    [InlineData(TransferLifecycleOutcome.RestoreRequiresSoftDeletion,
        TransferMessages.RestoreRequiresSoftDeletion)]
    [InlineData(TransferLifecycleOutcome.SettledStatementFrozen,
        TransferMessages.SettledStatementFrozen)]
    public async Task GivenLifecycleRefusal_WhenHandled_ThenCanonicalErrorIsReturned(
        TransferLifecycleOutcome outcome,
        string expected)
    {
        var result = await RestoreHandler(
            Profile(),
            new StubLifecycleStore(new(null, outcome))).HandleAsync(
                new RestoreTransferCommand { Id = Guid.NewGuid() });

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenDeleted_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycleStore(new(
            Guid.NewGuid(),
            TransferLifecycleOutcome.Succeeded));

        var result = await DeleteHandler(null, store).HandleAsync(
            new DeleteTransferCommand { Id = Guid.NewGuid() });

        Assert.Contains(TransferMessages.ProfileNotFound, result.Errors);
        Assert.False(store.DeleteCalled);
    }

    private static DeleteTransferCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        ITransferLifecycleStore store) => new(
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreTransferCommandHandler RestoreHandler(
        UserProfileSnapshot? profile,
        ITransferLifecycleStore store) => new(
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubLifecycleStore(TransferLifecycleResult result)
        : ITransferLifecycleStore
    {
        public bool DeleteCalled { get; private set; }
        public bool RestoreCalled { get; private set; }
        public Guid? UserId { get; private set; }
        public Guid? TransferId { get; private set; }
        public DateTimeOffset? ChangedAt { get; private set; }

        public Task<TransferLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            UserId = userId;
            TransferId = id;
            ChangedAt = changedAt;
            return Task.FromResult(result);
        }

        public Task<TransferLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            RestoreCalled = true;
            UserId = userId;
            TransferId = id;
            ChangedAt = changedAt;
            return Task.FromResult(result);
        }
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

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
