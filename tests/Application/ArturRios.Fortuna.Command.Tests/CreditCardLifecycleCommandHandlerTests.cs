using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreditCardLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 19, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedCard_WhenDeleted_ThenOutstandingAmountIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore
        {
            SoftDeleteResult = Success(id, 125.50m)
        };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteCreditCardCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(125.50m, result.Data?.OutstandingAmount);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Equal(id, store.CardId);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(CreditCardMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedCard_WhenRestored_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { RestoreResult = Success(id, 40m) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreCreditCardCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(40m, result.Data?.OutstandingAmount);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(CreditCardMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedCard_WhenHardDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { HardDeleteResult = Success(id) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteCreditCardCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Contains(CreditCardMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingCard_WhenDeleted_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            SoftDeleteResult = Failure(CreditCardLifecycleOutcome.NotFound)
        };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteCreditCardCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.NotFound, result.Errors);
    }

    [UnitTheory]
    [InlineData(CreditCardLifecycleOutcome.RestoreRequiresSoftDeletion,
        CreditCardMessages.RestoreRequiresSoftDeletion)]
    [InlineData(CreditCardLifecycleOutcome.DuplicateName,
        CreditCardMessages.DuplicateName)]
    public async Task GivenRestoreConflict_WhenRestored_ThenConflictIsReturned(
        CreditCardLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { RestoreResult = Failure(outcome) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreCreditCardCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitTheory]
    [InlineData(CreditCardLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        CreditCardMessages.HardDeleteRequiresSoftDeletion)]
    [InlineData(CreditCardLifecycleOutcome.HardDeleteHasLiveTransactions,
        CreditCardMessages.HardDeleteHasLiveTransactions)]
    public async Task GivenHardDeleteConflict_WhenHardDeleted_ThenConflictIsReturned(
        CreditCardLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { HardDeleteResult = Failure(outcome) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteCreditCardCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenLifecycleRequested_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycleStore();

        var result = await DeleteHandler(null, store).HandleAsync(
            new DeleteCreditCardCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenDeleted_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var profiles = new StubUserProfileReader(profile);
        var store = new StubLifecycleStore { SoftDeleteResult = Success(Guid.NewGuid()) };
        var handler = new DeleteCreditCardCommandHandler(
            new StubActorAccessor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteCreditCardCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static DeleteCreditCardCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        ICreditCardLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreCreditCardCommandHandler RestoreHandler(
        UserProfileSnapshot profile,
        ICreditCardLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static HardDeleteCreditCardCommandHandler HardDeleteHandler(
        UserProfileSnapshot profile,
        ICreditCardLifecycleStore store) => new(
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

    private static CreditCardLifecycleResult Success(Guid id, decimal outstanding = 0m) => new(
        id,
        CreditCardLifecycleOutcome.Succeeded,
        "BRL",
        outstanding);

    private static CreditCardLifecycleResult Failure(CreditCardLifecycleOutcome outcome) =>
        new(null, outcome);

    private sealed class StubLifecycleStore : ICreditCardLifecycleStore
    {
        public CreditCardLifecycleResult SoftDeleteResult { get; init; } = Success(Guid.NewGuid());
        public CreditCardLifecycleResult RestoreResult { get; init; } = Success(Guid.NewGuid());
        public CreditCardLifecycleResult HardDeleteResult { get; init; } = Success(Guid.NewGuid());
        public Guid? UserId { get; private set; }
        public Guid? CardId { get; private set; }
        public DateTimeOffset? ChangedAt { get; private set; }

        public Task<CreditCardLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(SoftDeleteResult);
        }

        public Task<CreditCardLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(RestoreResult);
        }

        public Task<CreditCardLifecycleResult> HardDeleteAsync(
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
            CardId = id;
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
