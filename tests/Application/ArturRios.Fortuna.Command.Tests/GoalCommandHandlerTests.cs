using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class GoalCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidGoal_WhenCreated_ThenSnapshotAndProgressAreReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubGoalStore
        {
            Result = new GoalMutationResult(snapshot, GoalMutationOutcome.Succeeded)
        };
        var handler = new CreateGoalCommandHandler(
            new CreateGoalCommandValidator(new FixedTimeProvider(Now)),
            Actor(profile), new StubProfileReader(profile), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCreate(snapshot.Accounts.Single().Id));

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(250m, result.Data?.CurrentProgress.CurrentAmount);
        Assert.Equal(profile.Id, store.Creation?.UserId);
        Assert.Equal("BRL", store.Creation?.CurrencyCode);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(GoalMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidGoal_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubGoalStore();
        var handler = new CreateGoalCommandHandler(
            new CreateGoalCommandValidator(new FixedTimeProvider(Now)),
            Actor(Profile()), new StubProfileReader(Profile()), store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateGoalCommand());

        Assert.False(result.Success);
        Assert.Null(store.Creation);
        Assert.Contains(GoalMessages.TargetAmountMustBePositive, result.Errors);
    }

    [UnitTheory]
    [InlineData(GoalMutationOutcome.NotFound, GoalMessages.NotFound)]
    [InlineData(GoalMutationOutcome.ResourceNotFound, GoalMessages.ResourceNotFound)]
    [InlineData(GoalMutationOutcome.CurrencyNotFound, GoalMessages.CurrencyNotSupported)]
    public async Task GivenRejectedUpdate_WhenHandled_ThenExpectedErrorIsReturned(
        GoalMutationOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubGoalStore
        {
            Result = new GoalMutationResult(null, outcome)
        };
        var handler = new UpdateGoalCommandHandler(
            new UpdateGoalCommandValidator(new FixedTimeProvider(Now)),
            Actor(profile), new StubProfileReader(profile), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new UpdateGoalCommand
        {
            Id = Guid.NewGuid(),
            Name = "Retirement",
            TargetAmount = 1_000m,
            CurrencyCode = "brl",
            TargetDate = new DateOnly(2027, 1, 1),
            InvestmentIds = [Guid.NewGuid()]
        });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
        Assert.Equal(Now, store.Update?.UpdatedAt);
    }

    [UnitFact]
    public async Task GivenGoal_WhenDeleted_ThenCurrentDateIsPassedToStore()
    {
        var profile = Profile();
        var store = new StubGoalStore
        {
            Result = new GoalMutationResult(Snapshot(true), GoalMutationOutcome.Succeeded)
        };
        var handler = new DeleteGoalCommandHandler(
            Actor(profile), new StubProfileReader(profile), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteGoalCommand { Id = Guid.NewGuid() });

        Assert.True(result.Success);
        Assert.True(result.Data?.IsDeleted);
        Assert.Equal(Now, store.DeletedAt);
        Assert.Equal(new DateOnly(2026, 9, 6), store.AsOf);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubGoalStore();
        var handler = new CreateGoalCommandHandler(
            new CreateGoalCommandValidator(new FixedTimeProvider(Now)),
            Actor(null), new StubProfileReader(null), store, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCreate(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Null(store.Creation);
        Assert.Contains(GoalMessages.ProfileNotFound, result.Errors);
    }

    private static CreateGoalCommand ValidCreate(Guid accountId) => new()
    {
        Name = "  Home  ",
        TargetAmount = 500m,
        CurrencyCode = "brl",
        TargetDate = new DateOnly(2027, 1, 1),
        AccountIds = [accountId]
    };

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static GoalSnapshot Snapshot(bool isDeleted = false) => new(
        Guid.NewGuid(), "Home", 500m, "BRL", new DateOnly(2027, 1, 1),
        [new GoalResourceSnapshot(Guid.NewGuid(), "Savings")], [],
        new GoalProgressSnapshot(250m, 250m, 0.5m, false, true),
        isDeleted, Now, Now);

    private sealed class StubGoalStore : IGoalStore, IGoalUpdater, IGoalLifecycleStore
    {
        public GoalMutationResult Result { get; init; } = new(
            Snapshot(), GoalMutationOutcome.Succeeded);
        public GoalCreation? Creation { get; private set; }
        public GoalUpdate? Update { get; private set; }
        public DateTimeOffset? DeletedAt { get; private set; }
        public DateOnly? AsOf { get; private set; }

        public Task<GoalMutationResult> CreateAsync(
            GoalCreation creation, CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(Result);
        }

        public Task<GoalMutationResult> UpdateAsync(
            GoalUpdate update, CancellationToken cancellationToken)
        {
            Update = update;
            return Task.FromResult(Result);
        }

        public Task<GoalMutationResult> SoftDeleteAsync(
            Guid userId, Guid id, DateTimeOffset changedAt, DateOnly asOf,
            CancellationToken cancellationToken)
        {
            DeletedAt = changedAt;
            AsOf = asOf;
            return Task.FromResult(Result);
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
