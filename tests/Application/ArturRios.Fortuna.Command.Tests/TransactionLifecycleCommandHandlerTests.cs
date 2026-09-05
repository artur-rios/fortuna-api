using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class TransactionLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTransaction_WhenDeleted_ThenIdentifierAndMessageAreReturned()
    {
        var profile = Profile();
        var transactionId = Guid.NewGuid();
        var store = SucceedingStore(transactionId);

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteTransactionCommand { Id = transactionId });

        Assert.True(result.Success);
        Assert.Equal(transactionId, result.Data?.Id);
        Assert.Equal((profile.Id, transactionId, Now), store.SoftDelete);
        Assert.Contains(TransactionMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedTransaction_WhenRestored_ThenIdentifierAndMessageAreReturned()
    {
        var profile = Profile();
        var transactionId = Guid.NewGuid();
        var store = SucceedingStore(transactionId);

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreTransactionCommand { Id = transactionId });

        Assert.True(result.Success);
        Assert.Equal(transactionId, result.Data?.Id);
        Assert.Equal((profile.Id, transactionId, Now), store.Restore);
        Assert.Contains(TransactionMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenSoftDeletedTransaction_WhenHardDeleted_ThenIdentifierAndMessageAreReturned()
    {
        var profile = Profile();
        var transactionId = Guid.NewGuid();
        var store = SucceedingStore(transactionId);

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteTransactionCommand { Id = transactionId });

        Assert.True(result.Success);
        Assert.Equal(transactionId, result.Data?.Id);
        Assert.Equal((profile.Id, transactionId), store.HardDelete);
        Assert.Contains(TransactionMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(TransactionLifecycleOutcome.NotFound, TransactionMessages.NotFound)]
    [InlineData(TransactionLifecycleOutcome.RestoreRequiresSoftDeletion,
        TransactionMessages.RestoreRequiresSoftDeletion)]
    [InlineData(TransactionLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        TransactionMessages.HardDeleteRequiresSoftDeletion)]
    [InlineData(TransactionLifecycleOutcome.SettledStatementFrozen,
        TransactionMessages.SettledStatementFrozen)]
    public async Task GivenLifecycleRefusal_WhenRequested_ThenCanonicalErrorIsReturned(
        TransactionLifecycleOutcome outcome,
        string expected)
    {
        var store = new StubTransactionLifecycleStore(outcome);
        var profile = Profile();

        var delete = await DeleteHandler(profile, store).HandleAsync(
            new DeleteTransactionCommand { Id = Guid.NewGuid() });
        var restore = await RestoreHandler(profile, store).HandleAsync(
            new RestoreTransactionCommand { Id = Guid.NewGuid() });
        var hardDelete = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteTransactionCommand { Id = Guid.NewGuid() });

        Assert.Contains(expected, delete.Errors);
        Assert.Contains(expected, restore.Errors);
        Assert.Contains(expected, hardDelete.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenLifecycleRequested_ThenStorageIsNotCalled()
    {
        var store = SucceedingStore(Guid.NewGuid());

        var delete = await DeleteHandler(null, store).HandleAsync(
            new DeleteTransactionCommand { Id = Guid.NewGuid() });
        var restore = await RestoreHandler(null, store).HandleAsync(
            new RestoreTransactionCommand { Id = Guid.NewGuid() });
        var hardDelete = await HardDeleteHandler(null, store).HandleAsync(
            new HardDeleteTransactionCommand { Id = Guid.NewGuid() });

        Assert.Contains(TransactionMessages.ProfileNotFound, delete.Errors);
        Assert.Contains(TransactionMessages.ProfileNotFound, restore.Errors);
        Assert.Contains(TransactionMessages.ProfileNotFound, hardDelete.Errors);
        Assert.Null(store.SoftDelete);
        Assert.Null(store.Restore);
        Assert.Null(store.HardDelete);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenDeleted_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile();
        var profiles = new StubProfileReader(profile);
        var store = SucceedingStore(Guid.NewGuid());
        var handler = new DeleteTransactionCommandHandler(
            new StubActor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new DeleteTransactionCommand { Id = Guid.NewGuid() });

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static DeleteTransactionCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        ITransactionLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreTransactionCommandHandler RestoreHandler(
        UserProfileSnapshot? profile,
        ITransactionLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static HardDeleteTransactionCommandHandler HardDeleteHandler(
        UserProfileSnapshot? profile,
        ITransactionLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store);

    private static StubActor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static StubTransactionLifecycleStore SucceedingStore(Guid id) => new(
        TransactionLifecycleOutcome.Succeeded,
        id);

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubTransactionLifecycleStore(
        TransactionLifecycleOutcome outcome,
        Guid? resultId = null) : ITransactionLifecycleStore
    {
        public (Guid UserId, Guid Id, DateTimeOffset ChangedAt)? SoftDelete { get; private set; }
        public (Guid UserId, Guid Id, DateTimeOffset ChangedAt)? Restore { get; private set; }
        public (Guid UserId, Guid Id)? HardDelete { get; private set; }

        public Task<TransactionLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            SoftDelete = (userId, id, changedAt);
            return Task.FromResult(new TransactionLifecycleResult(resultId, outcome));
        }

        public Task<TransactionLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Restore = (userId, id, changedAt);
            return Task.FromResult(new TransactionLifecycleResult(resultId, outcome));
        }

        public Task<TransactionLifecycleResult> HardDeleteAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken)
        {
            HardDelete = (userId, id);
            return Task.FromResult(new TransactionLifecycleResult(resultId, outcome));
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
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
