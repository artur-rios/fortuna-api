using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CounterpartyCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 2, 0, 0, TimeSpan.Zero);

    [UnitTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenCounterparty_WhenCreated_ThenCreationOrReuseIsReturned(bool reused)
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubCounterpartyStore
        {
            CreationResult = new CounterpartyCreationResult(
                snapshot,
                reused,
                CounterpartyMutationOutcome.Succeeded)
        };

        var result = await CreateHandler(profile, store).HandleAsync(new() { Name = " Shop " });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(reused, result.Data?.Reused);
        Assert.Equal(profile.Id, store.Creation?.UserId);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(
            reused
                ? CounterpartyMessages.ReusedSuccessfully
                : CounterpartyMessages.CreatedSuccessfully,
            result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidCounterparty_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubCounterpartyStore();

        var result = await CreateHandler(Profile(), store).HandleAsync(new());

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.NameRequired, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenDuplicateName_WhenUpdated_ThenConflictIsReturned()
    {
        var store = new StubCounterpartyStore
        {
            MutationResult = new CounterpartyMutationResult(
                null,
                CounterpartyMutationOutcome.DuplicateName)
        };

        var result = await UpdateHandler(Profile(), store).HandleAsync(new()
        {
            Id = Guid.NewGuid(),
            Name = "Duplicate"
        });

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.DuplicateName, result.Errors);
        Assert.NotNull(store.Update);
    }

    [UnitFact]
    public async Task GivenNewName_WhenUpdated_ThenSnapshotIsReturned()
    {
        var snapshot = Snapshot();
        var store = new StubCounterpartyStore
        {
            MutationResult = new CounterpartyMutationResult(
                snapshot,
                CounterpartyMutationOutcome.Succeeded)
        };

        var result = await UpdateHandler(Profile(), store).HandleAsync(new()
        {
            Id = snapshot.Id,
            Name = "Renamed"
        });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal("Renamed", store.Update?.Name);
        Assert.Contains(CounterpartyMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenCounterparty_WhenDeleted_ThenDeletedSnapshotIsReturned()
    {
        var snapshot = Snapshot(isDeleted: true);
        var store = new StubCounterpartyStore
        {
            MutationResult = new CounterpartyMutationResult(
                snapshot,
                CounterpartyMutationOutcome.Succeeded)
        };

        var result = await DeleteHandler(Profile(), store).HandleAsync(
            new DeleteCounterpartyCommand { Id = snapshot.Id });

        Assert.True(result.Success);
        Assert.True(result.Data?.IsDeleted);
        Assert.Equal(Now, store.DeletedAt);
        Assert.Contains(CounterpartyMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingCounterparty_WhenDeleted_ThenNotFoundIsReturned()
    {
        var store = new StubCounterpartyStore
        {
            MutationResult = new CounterpartyMutationResult(
                null,
                CounterpartyMutationOutcome.NotFound)
        };

        var result = await DeleteHandler(Profile(), store).HandleAsync(
            new DeleteCounterpartyCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenTwoCounterparties_WhenMerged_ThenReassignedCountIsReturned()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var store = new StubCounterpartyStore
        {
            MergeResult = new CounterpartyMergeResult(
                sourceId,
                targetId,
                4,
                CounterpartyMergeOutcome.Succeeded)
        };

        var result = await MergeHandler(Profile(), store).HandleAsync(new()
        {
            Id = sourceId,
            TargetId = targetId
        });

        Assert.True(result.Success);
        Assert.Equal(4, result.Data?.ReassignedTransactionCount);
        Assert.Equal(Now, store.Merge?.ChangedAt);
        Assert.Contains(CounterpartyMessages.MergedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CounterpartyMergeOutcome.NotFound)]
    [InlineData(CounterpartyMergeOutcome.SameCounterparty)]
    public async Task GivenInvalidMerge_WhenHandled_ThenExpectedErrorIsReturned(
        CounterpartyMergeOutcome outcome)
    {
        var store = new StubCounterpartyStore
        {
            MergeResult = new CounterpartyMergeResult(null, null, 0, outcome)
        };

        var result = await MergeHandler(Profile(), store).HandleAsync(new()
        {
            Id = Guid.NewGuid(),
            TargetId = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(
            outcome == CounterpartyMergeOutcome.NotFound
                ? CounterpartyMessages.NotFound
                : CounterpartyMessages.SameCounterparty,
            result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubCounterpartyStore();

        var result = await CreateHandler(null, store).HandleAsync(new() { Name = "Shop" });

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    private static CreateCounterpartyCommandHandler CreateHandler(
        UserProfileSnapshot? profile,
        ICounterpartyStore store) => new(
        new CreateCounterpartyCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static UpdateCounterpartyCommandHandler UpdateHandler(
        UserProfileSnapshot profile,
        ICounterpartyUpdater store) => new(
        new UpdateCounterpartyCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static DeleteCounterpartyCommandHandler DeleteHandler(
        UserProfileSnapshot profile,
        ICounterpartyLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static MergeCounterpartiesCommandHandler MergeHandler(
        UserProfileSnapshot profile,
        ICounterpartyMerger store) => new(
        new MergeCounterpartiesCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static CounterpartySnapshot Snapshot(bool isDeleted = false) => new(
        Guid.NewGuid(), "Shop", isDeleted, Now, Now);

    private sealed class StubCounterpartyStore : ICounterpartyStore,
        ICounterpartyUpdater,
        ICounterpartyLifecycleStore,
        ICounterpartyMerger
    {
        public CounterpartyCreationResult CreationResult { get; init; } = new(
            Snapshot(), false, CounterpartyMutationOutcome.Succeeded);
        public CounterpartyMutationResult MutationResult { get; init; } = new(
            Snapshot(), CounterpartyMutationOutcome.Succeeded);
        public CounterpartyMergeResult MergeResult { get; init; } = new(
            Guid.NewGuid(), Guid.NewGuid(), 0, CounterpartyMergeOutcome.Succeeded);
        public CounterpartyCreation? Creation { get; private set; }
        public CounterpartyUpdate? Update { get; private set; }
        public DateTimeOffset? DeletedAt { get; private set; }
        public CounterpartyMerge? Merge { get; private set; }

        public Task<CounterpartyCreationResult> CreateAsync(
            CounterpartyCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(CreationResult);
        }

        public Task<CounterpartyMutationResult> UpdateAsync(
            CounterpartyUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
            return Task.FromResult(MutationResult);
        }

        public Task<CounterpartyMutationResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            DeletedAt = changedAt;
            return Task.FromResult(MutationResult);
        }

        public Task<CounterpartyMergeResult> MergeAsync(
            CounterpartyMerge merge,
            CancellationToken cancellationToken)
        {
            Merge = merge;
            return Task.FromResult(MergeResult);
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

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
