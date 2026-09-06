using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class TagCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 1, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidTag_WhenCreated_ThenSnapshotIsReturned()
    {
        var profile = Profile();
        var tag = Snapshot();
        var store = new StubTagStore
        {
            CreationResult = new TagCreationResult(tag, TagMutationOutcome.Succeeded)
        };

        var result = await CreateHandler(profile, store).HandleAsync(new() { Name = " Food " });

        Assert.True(result.Success);
        Assert.Equal(tag.Id, result.Data?.Id);
        Assert.Equal(profile.Id, store.Creation?.UserId);
        Assert.Equal(" Food ", store.Creation?.Name);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(TagMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidTag_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubTagStore();

        var result = await CreateHandler(Profile(), store).HandleAsync(new());

        Assert.False(result.Success);
        Assert.Contains(TagMessages.NameRequired, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenDuplicateTag_WhenUpdated_ThenConflictIsReturned()
    {
        var store = new StubTagStore
        {
            UpdateResult = new TagUpdateResult(null, TagMutationOutcome.DuplicateName)
        };

        var result = await UpdateHandler(Profile(), store).HandleAsync(new()
        {
            Id = Guid.NewGuid(),
            Name = "Duplicate"
        });

        Assert.False(result.Success);
        Assert.Contains(TagMessages.DuplicateName, result.Errors);
        Assert.NotNull(store.Update);
    }

    [UnitFact]
    public async Task GivenAttachedTag_WhenDeleted_ThenDetachedCountIsReturned()
    {
        var tag = Snapshot(isDeleted: true);
        var store = new StubTagStore
        {
            DeletionResult = new TagDeletionResult(tag, 3, TagMutationOutcome.Succeeded)
        };

        var result = await DeleteHandler(Profile(), store).HandleAsync(
            new DeleteTagCommand { Id = tag.Id });

        Assert.True(result.Success);
        Assert.True(result.Data?.IsDeleted);
        Assert.Equal(3, result.Data?.DetachedTransactionCount);
        Assert.Equal(Now, store.DeletedAt);
    }

    [UnitTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenTagAssignment_WhenAttached_ThenIdempotentStateIsReturned(bool changed)
    {
        var transactionId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var store = new StubTagStore
        {
            AttachResult = Assignment(
                TransactionTagAssignmentOutcome.Succeeded,
                transactionId,
                tagId,
                isAttached: true,
                changed,
                2)
        };

        var result = await AttachHandler(Profile(), store).HandleAsync(new()
        {
            Id = transactionId,
            TagId = tagId
        });

        Assert.True(result.Success);
        Assert.True(result.Data?.IsAttached);
        Assert.Equal(2, result.Data?.TagCount);
        Assert.Contains(
            changed ? TagMessages.AttachedSuccessfully : TagMessages.AlreadyAttached,
            result.Messages);
    }

    [UnitFact]
    public async Task GivenAttachedTag_WhenDetached_ThenDetachedStateIsReturned()
    {
        var transactionId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var store = new StubTagStore
        {
            DetachResult = Assignment(
                TransactionTagAssignmentOutcome.Succeeded,
                transactionId,
                tagId,
                isAttached: false,
                changed: true,
                0)
        };

        var result = await DetachHandler(Profile(), store).HandleAsync(new()
        {
            Id = transactionId,
            TagId = tagId
        });

        Assert.True(result.Success);
        Assert.False(result.Data?.IsAttached);
        Assert.Contains(TagMessages.DetachedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMaximumTags_WhenAttached_ThenConfiguredMaximumIsReturned()
    {
        var store = new StubTagStore
        {
            AttachResult = Assignment(TransactionTagAssignmentOutcome.MaximumExceeded)
        };

        var result = await AttachHandler(Profile(), store, maximum: 2).HandleAsync(new()
        {
            Id = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(TagMessages.MaximumExceeded, result.Errors);
        Assert.Contains(TagMessages.MaximumAllowed(2), result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingAssignment_WhenDetached_ThenNotFoundIsReturned()
    {
        var store = new StubTagStore
        {
            DetachResult = Assignment(TransactionTagAssignmentOutcome.NotFound)
        };

        var result = await DetachHandler(Profile(), store).HandleAsync(new()
        {
            Id = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(TagMessages.AssignmentNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenTagCreated_ThenStoreIsNotCalled()
    {
        var store = new StubTagStore();

        var result = await CreateHandler(null, store).HandleAsync(new() { Name = "Food" });

        Assert.False(result.Success);
        Assert.Contains(TagMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    private static CreateTagCommandHandler CreateHandler(
        UserProfileSnapshot? profile,
        ITagStore store) => new(
        new CreateTagCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static UpdateTagCommandHandler UpdateHandler(
        UserProfileSnapshot profile,
        ITagUpdater store) => new(
        new UpdateTagCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static DeleteTagCommandHandler DeleteHandler(
        UserProfileSnapshot profile,
        ITagLifecycleStore store) => new(
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static AttachTransactionTagCommandHandler AttachHandler(
        UserProfileSnapshot profile,
        ITransactionTagStore store,
        int maximum = 50) => new(
        new AttachTransactionTagCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new TagOptions(maximum),
        new FixedTimeProvider(Now));

    private static DetachTransactionTagCommandHandler DetachHandler(
        UserProfileSnapshot profile,
        ITransactionTagStore store) => new(
        new DetachTransactionTagCommandValidator(),
        Actor(profile),
        new StubProfileReader(profile),
        store,
        new TagOptions(50),
        new FixedTimeProvider(Now));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private static TagSnapshot Snapshot(bool isDeleted = false) => new(
        Guid.NewGuid(),
        "Food",
        isDeleted,
        Now,
        Now);

    private static TransactionTagAssignmentResult Assignment(
        TransactionTagAssignmentOutcome outcome,
        Guid? transactionId = null,
        Guid? tagId = null,
        bool isAttached = false,
        bool changed = false,
        int count = 0) => new(
            transactionId,
            tagId,
            isAttached,
            changed,
            count,
            outcome);

    private sealed class StubTagStore : ITagStore, ITagUpdater, ITagLifecycleStore,
        ITransactionTagStore
    {
        public TagCreationResult CreationResult { get; init; } =
            new(Snapshot(), TagMutationOutcome.Succeeded);
        public TagUpdateResult UpdateResult { get; init; } =
            new(Snapshot(), TagMutationOutcome.Succeeded);
        public TagDeletionResult DeletionResult { get; init; } =
            new(Snapshot(true), 0, TagMutationOutcome.Succeeded);
        public TransactionTagAssignmentResult AttachResult { get; init; } =
            Assignment(TransactionTagAssignmentOutcome.Succeeded);
        public TransactionTagAssignmentResult DetachResult { get; init; } =
            Assignment(TransactionTagAssignmentOutcome.Succeeded);
        public TagCreation? Creation { get; private set; }
        public TagUpdate? Update { get; private set; }
        public DateTimeOffset? DeletedAt { get; private set; }

        public Task<TagCreationResult> CreateAsync(
            TagCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(CreationResult);
        }

        public Task<TagUpdateResult> UpdateAsync(
            TagUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
            return Task.FromResult(UpdateResult);
        }

        public Task<TagDeletionResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            DeletedAt = changedAt;
            return Task.FromResult(DeletionResult);
        }

        public Task<TransactionTagAssignmentResult> AttachAsync(
            TransactionTagAssignment assignment,
            CancellationToken cancellationToken) => Task.FromResult(AttachResult);

        public Task<TransactionTagAssignmentResult> DetachAsync(
            TransactionTagAssignment assignment,
            CancellationToken cancellationToken) => Task.FromResult(DetachResult);
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
