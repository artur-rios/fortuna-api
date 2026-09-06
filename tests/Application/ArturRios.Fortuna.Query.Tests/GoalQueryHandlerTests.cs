using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class GoalQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedGoals_WhenListed_ThenSnapshotsAreReturned()
    {
        var profile = Profile();
        var store = new StubGoalReader([Snapshot()]);
        var handler = new ListGoalsQueryHandler(
            Actor(profile), new StubProfileReader(profile), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ListGoalsQuery { IncludeDeleted = true });

        Assert.True(result.Success);
        var goal = Assert.Single(result.Data!.Goals);
        Assert.Equal(250m, goal.CurrentProgress.CurrentAmount);
        Assert.Equal(profile.Id, store.UserId);
        Assert.True(store.IncludeDeleted);
        Assert.Equal(new DateOnly(2026, 9, 6), store.AsOf);
        Assert.Contains(GoalMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenOwnedGoal_WhenRetrieved_ThenSnapshotIsReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubGoalReader([], snapshot);
        var handler = new GetGoalByIdQueryHandler(
            Actor(profile), new StubProfileReader(profile), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetGoalByIdQuery { Id = snapshot.Id });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal("Savings", result.Data?.Accounts.Single().Name);
        Assert.Equal(snapshot.Id, store.GoalId);
        Assert.Contains(GoalMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingGoal_WhenRetrieved_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var handler = new GetGoalByIdQueryHandler(
            Actor(profile), new StubProfileReader(profile), new StubGoalReader([]),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetGoalByIdQuery { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(GoalMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenListed_ThenStoreIsNotCalled()
    {
        var store = new StubGoalReader([]);
        var handler = new ListGoalsQueryHandler(
            Actor(null), new StubProfileReader(null), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ListGoalsQuery());

        Assert.False(result.Success);
        Assert.Null(store.UserId);
        Assert.Contains(GoalMessages.ProfileNotFound, result.Errors);
    }

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static GoalSnapshot Snapshot() => new(
        Guid.NewGuid(), "Home", 500m, "BRL", new DateOnly(2027, 1, 1),
        [new GoalResourceSnapshot(Guid.NewGuid(), "Savings")], [],
        new GoalProgressSnapshot(250m, 250m, 0.5m, false, true),
        false, Now, Now);

    private sealed class StubGoalReader(
        IReadOnlyCollection<GoalSnapshot> goals,
        GoalSnapshot? goal = null) : IGoalReader
    {
        public Guid? UserId { get; private set; }
        public Guid? GoalId { get; private set; }
        public bool IncludeDeleted { get; private set; }
        public DateOnly? AsOf { get; private set; }

        public Task<IReadOnlyCollection<GoalSnapshot>> ListAsync(
            Guid userId, bool includeDeleted, DateOnly asOf,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            AsOf = asOf;
            return Task.FromResult(goals);
        }

        public Task<GoalSnapshot?> FindByIdAsync(
            Guid userId, Guid id, bool includeDeleted, DateOnly asOf,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            GoalId = id;
            IncludeDeleted = includeDeleted;
            AsOf = asOf;
            return Task.FromResult(goal);
        }
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
