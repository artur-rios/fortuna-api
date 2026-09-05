using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class InvestmentLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 18, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedInvestment_WhenDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { SoftDeleteResult = Success(id) };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteInvestmentCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Equal(id, store.InvestmentId);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(InvestmentMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedInvestment_WhenRestored_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { RestoreResult = Success(id) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreInvestmentCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(InvestmentMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedUnreferencedInvestment_WhenHardDeleted_ThenSuccessIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { HardDeleteResult = Success(id) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteInvestmentCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Contains(InvestmentMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingInvestment_WhenDeleted_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            SoftDeleteResult = Failure(InvestmentLifecycleOutcome.NotFound)
        };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteInvestmentCommand { Id = Guid.NewGuid() });

        Assert.Contains(InvestmentMessages.NotFound, result.Errors);
    }

    [UnitTheory]
    [InlineData(InvestmentLifecycleOutcome.RestoreRequiresSoftDeletion,
        InvestmentMessages.RestoreRequiresSoftDeletion)]
    [InlineData(InvestmentLifecycleOutcome.DuplicateInstrument,
        InvestmentMessages.DuplicateInstrument)]
    public async Task GivenRestoreConflict_WhenRestored_ThenConflictIsReturned(
        InvestmentLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { RestoreResult = Failure(outcome) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreInvestmentCommand { Id = Guid.NewGuid() });

        Assert.Contains(expectedError, result.Errors);
    }

    [UnitTheory]
    [InlineData(InvestmentLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        InvestmentMessages.HardDeleteRequiresSoftDeletion)]
    [InlineData(InvestmentLifecycleOutcome.NotFound, InvestmentMessages.NotFound)]
    public async Task GivenHardDeleteConflict_WhenHardDeleted_ThenConflictIsReturned(
        InvestmentLifecycleOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubLifecycleStore { HardDeleteResult = Failure(outcome) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteInvestmentCommand { Id = Guid.NewGuid() });

        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenLiveGoalReference_WhenHardDeleted_ThenConflictNamesGoal()
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            HardDeleteResult = new InvestmentLifecycleResult(
                null,
                InvestmentLifecycleOutcome.HardDeleteHasLiveGoal,
                "Home Deposit")
        };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteInvestmentCommand { Id = Guid.NewGuid() });

        Assert.Contains(InvestmentMessages.HardDeleteHasLiveGoal, result.Errors);
        Assert.Contains(InvestmentMessages.ReferencingGoal("Home Deposit"), result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenLifecycleRequested_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycleStore();

        var result = await DeleteHandler(null, store).HandleAsync(
            new DeleteInvestmentCommand { Id = Guid.NewGuid() });

        Assert.Contains(InvestmentMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenDeleted_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var profiles = new StubProfileReader(profile);
        var store = new StubLifecycleStore { SoftDeleteResult = Success(Guid.NewGuid()) };
        var handler = new DeleteInvestmentCommandHandler(
            new StubActor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteInvestmentCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static DeleteInvestmentCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        IInvestmentLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreInvestmentCommandHandler RestoreHandler(
        UserProfileSnapshot profile,
        IInvestmentLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static HardDeleteInvestmentCommandHandler HardDeleteHandler(
        UserProfileSnapshot profile,
        IInvestmentLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store);

    private static StubActor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile(Guid? externalSubject = default) => new(
        Guid.NewGuid(),
        externalSubject ?? Guid.NewGuid(),
        "Investment Owner",
        "BRL",
        false,
        Now,
        Now);

    private static InvestmentLifecycleResult Success(Guid id) => new(
        id,
        InvestmentLifecycleOutcome.Succeeded);

    private static InvestmentLifecycleResult Failure(InvestmentLifecycleOutcome outcome) =>
        new(null, outcome);

    private sealed class StubLifecycleStore : IInvestmentLifecycleStore
    {
        public InvestmentLifecycleResult SoftDeleteResult { get; init; } = Success(Guid.NewGuid());
        public InvestmentLifecycleResult RestoreResult { get; init; } = Success(Guid.NewGuid());
        public InvestmentLifecycleResult HardDeleteResult { get; init; } = Success(Guid.NewGuid());
        public Guid? UserId { get; private set; }
        public Guid? InvestmentId { get; private set; }
        public DateTimeOffset? ChangedAt { get; private set; }

        public Task<InvestmentLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(SoftDeleteResult);
        }

        public Task<InvestmentLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(RestoreResult);
        }

        public Task<InvestmentLifecycleResult> HardDeleteAsync(
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
            InvestmentId = id;
            ChangedAt = changedAt;
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
