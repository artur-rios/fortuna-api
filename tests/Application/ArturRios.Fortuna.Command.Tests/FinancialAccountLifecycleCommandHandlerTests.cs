using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class FinancialAccountLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 16, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedAccount_WhenDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore
        {
            SoftDeleteResult = Success(id)
        };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteFinancialAccountCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Equal(id, store.AccountId);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(FinancialAccountMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedAccount_WhenRestored_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { RestoreResult = Success(id) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreFinancialAccountCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(FinancialAccountMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedUnreferencedAccount_WhenHardDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { HardDeleteResult = Success(id) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteFinancialAccountCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Contains(FinancialAccountMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingAccount_WhenDeleted_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            SoftDeleteResult = Failure(FinancialAccountLifecycleOutcome.NotFound)
        };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteFinancialAccountCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.NotFound, result.Errors);
    }

    [UnitTheory]
    [InlineData(FinancialAccountLifecycleOutcome.RestoreRequiresSoftDeletion,
        FinancialAccountMessages.RestoreRequiresSoftDeletion)]
    [InlineData(FinancialAccountLifecycleOutcome.DuplicateName,
        FinancialAccountMessages.DuplicateName)]
    public async Task GivenRestoreConflict_WhenRestored_ThenConflictIsReturned(
        FinancialAccountLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { RestoreResult = Failure(outcome) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreFinancialAccountCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitTheory]
    [InlineData(FinancialAccountLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        FinancialAccountMessages.HardDeleteRequiresSoftDeletion)]
    [InlineData(FinancialAccountLifecycleOutcome.HardDeleteHasLiveTransactions,
        FinancialAccountMessages.HardDeleteHasLiveTransactions)]
    public async Task GivenHardDeleteConflict_WhenHardDeleted_ThenConflictIsReturned(
        FinancialAccountLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { HardDeleteResult = Failure(outcome) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteFinancialAccountCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenLifecycleRequested_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycleStore();

        var result = await DeleteHandler(null, store).HandleAsync(
            new DeleteFinancialAccountCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenDeleted_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var profiles = new StubUserProfileReader(profile);
        var store = new StubLifecycleStore { SoftDeleteResult = Success(Guid.NewGuid()) };
        var handler = new DeleteFinancialAccountCommandHandler(
            new StubActorAccessor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteFinancialAccountCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static DeleteFinancialAccountCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        IFinancialAccountLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreFinancialAccountCommandHandler RestoreHandler(
        UserProfileSnapshot profile,
        IFinancialAccountLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static HardDeleteFinancialAccountCommandHandler HardDeleteHandler(
        UserProfileSnapshot profile,
        IFinancialAccountLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store);

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile(Guid? externalSubject = default) => new(
        Guid.NewGuid(),
        externalSubject ?? Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private static FinancialAccountLifecycleResult Success(Guid id) => new(
        id,
        FinancialAccountLifecycleOutcome.Succeeded);

    private static FinancialAccountLifecycleResult Failure(FinancialAccountLifecycleOutcome outcome) =>
        new(null, outcome);

    private sealed class StubLifecycleStore : IFinancialAccountLifecycleStore
    {
        public FinancialAccountLifecycleResult SoftDeleteResult { get; init; } = Success(Guid.NewGuid());
        public FinancialAccountLifecycleResult RestoreResult { get; init; } = Success(Guid.NewGuid());
        public FinancialAccountLifecycleResult HardDeleteResult { get; init; } = Success(Guid.NewGuid());
        public Guid? UserId { get; private set; }
        public Guid? AccountId { get; private set; }
        public DateTimeOffset? ChangedAt { get; private set; }

        public Task<FinancialAccountLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(SoftDeleteResult);
        }

        public Task<FinancialAccountLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(RestoreResult);
        }

        public Task<FinancialAccountLifecycleResult> HardDeleteAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, null);
            return Task.FromResult(HardDeleteResult);
        }

        private void Capture(Guid userId, Guid id, DateTimeOffset? changedAt)
        {
            UserId = userId;
            AccountId = id;
            ChangedAt = changedAt;
        }
    }

    private sealed class StubUserProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
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

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
