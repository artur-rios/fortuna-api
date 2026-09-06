using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CategoryLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 0, 15, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedCategory_WhenDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { SoftDeleteResult = Success(id) };

        var result = await DeleteHandler(profile, store).HandleAsync(
            new DeleteCategoryCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Equal(id, store.CategoryId);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(CategoryMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedCategory_WhenRestored_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { RestoreResult = Success(id) };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreCategoryCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(Now, store.ChangedAt);
        Assert.Contains(CategoryMessages.RestoredSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenUnreferencedCategory_WhenHardDeleted_ThenSuccessfulResultIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore { HardDeleteResult = Success(id) };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteCategoryCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Contains(CategoryMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CategoryLifecycleOutcome.NotFound, CategoryMessages.NotFound)]
    [InlineData(CategoryLifecycleOutcome.RestoreRequiresSoftDeletion,
        CategoryMessages.RestoreRequiresSoftDeletion)]
    [InlineData(CategoryLifecycleOutcome.DuplicateSiblingName,
        CategoryMessages.DuplicateSiblingName)]
    public async Task GivenRestoreRefusal_WhenRestored_ThenExpectedErrorIsReturned(
        CategoryLifecycleOutcome outcome,
        string expected)
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            RestoreResult = new CategoryLifecycleResult(null, outcome)
        };

        var result = await RestoreHandler(profile, store).HandleAsync(
            new RestoreCategoryCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitTheory]
    [InlineData(CategoryLifecycleOutcome.NotFound, CategoryMessages.NotFound)]
    [InlineData(CategoryLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        CategoryMessages.HardDeleteRequiresSoftDeletion)]
    public async Task GivenHardDeleteRefusal_WhenHardDeleted_ThenExpectedErrorIsReturned(
        CategoryLifecycleOutcome outcome,
        string expected)
    {
        var profile = Profile();
        var store = new StubLifecycleStore
        {
            HardDeleteResult = new CategoryLifecycleResult(null, outcome)
        };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteCategoryCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenLiveReferences_WhenHardDeleted_ThenCountIsReturned()
    {
        var profile = Profile();
        var id = Guid.NewGuid();
        var store = new StubLifecycleStore
        {
            HardDeleteResult = new CategoryLifecycleResult(
                id,
                CategoryLifecycleOutcome.HardDeleteHasLiveTransactions,
                4)
        };

        var result = await HardDeleteHandler(profile, store).HandleAsync(
            new HardDeleteCategoryCommand { Id = id });

        Assert.False(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.Equal(4, result.Data?.LiveTransactionCount);
        Assert.Contains(CategoryMessages.HardDeleteHasLiveTransactions, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenDeleted_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycleStore();

        var result = await DeleteHandler(null, store).HandleAsync(
            new DeleteCategoryCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenDeleted_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var profiles = new StubUserProfileReader(profile);
        var store = new StubLifecycleStore { SoftDeleteResult = Success(Guid.NewGuid()) };
        var handler = new DeleteCategoryCommandHandler(
            new StubActorAccessor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteCategoryCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static DeleteCategoryCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        ICategoryLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RestoreCategoryCommandHandler RestoreHandler(
        UserProfileSnapshot profile,
        ICategoryLifecycleStore store) => new(
        Actor(profile),
        new StubUserProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static HardDeleteCategoryCommandHandler HardDeleteHandler(
        UserProfileSnapshot profile,
        ICategoryLifecycleStore store) => new(
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

    private static CategoryLifecycleResult Success(Guid id) => new(
        id,
        CategoryLifecycleOutcome.Succeeded);

    private sealed class StubLifecycleStore : ICategoryLifecycleStore
    {
        public CategoryLifecycleResult SoftDeleteResult { get; init; } = Success(Guid.NewGuid());
        public CategoryLifecycleResult RestoreResult { get; init; } = Success(Guid.NewGuid());
        public CategoryLifecycleResult HardDeleteResult { get; init; } = Success(Guid.NewGuid());
        public Guid? UserId { get; private set; }
        public Guid? CategoryId { get; private set; }
        public DateTimeOffset? ChangedAt { get; private set; }

        public Task<CategoryLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(SoftDeleteResult);
        }

        public Task<CategoryLifecycleResult> RestoreAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Capture(userId, id, changedAt);
            return Task.FromResult(RestoreResult);
        }

        public Task<CategoryLifecycleResult> HardDeleteAsync(
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
            CategoryId = id;
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
