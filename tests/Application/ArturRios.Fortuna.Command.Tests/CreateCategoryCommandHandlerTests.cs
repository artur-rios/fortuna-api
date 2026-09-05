using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateCategoryCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 18, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidNestedCategory_WhenCreated_ThenNormalizedCategoryIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var store = new StubCategoryStore(new CategoryCreationResult(
            new CategorySnapshot(
                categoryId, userId, "Dining", parentId, false, Now, Now),
            CategoryCreationOutcome.Succeeded));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(new()
        {
            Name = "  Dining  ",
            ParentId = parentId
        });

        Assert.True(result.Success);
        Assert.Equal(categoryId, result.Data!.Id);
        Assert.Equal("Dining", result.Data.Name);
        Assert.Equal(parentId, result.Data.ParentId);
        Assert.Equal(Now, result.Data.CreatedAt);
        Assert.Equal(Now, result.Data.UpdatedAt);
        Assert.Equal(userId, store.Creation!.UserId);
        Assert.Equal("Dining", store.Creation.Name);
        Assert.Equal(parentId, store.Creation.ParentId);
        Assert.Equal(Now, store.Creation.CreatedAt);
        Assert.Contains(CategoryMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CategoryCreationOutcome.ParentNotFound, CategoryMessages.ParentNotFound)]
    [InlineData(CategoryCreationOutcome.DuplicateSiblingName, CategoryMessages.DuplicateSiblingName)]
    [InlineData(CategoryCreationOutcome.CycleDetected, CategoryMessages.CycleDetected)]
    public async Task GivenStoreRefusal_WhenCreated_ThenExpectedErrorIsReturned(
        CategoryCreationOutcome outcome,
        string expected)
    {
        var subject = Guid.NewGuid();
        var store = new StubCategoryStore(new CategoryCreationResult(null, outcome));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(new CreateCategoryCommand { Name = "Dining" });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenNotFoundIsReturned()
    {
        var store = new StubCategoryStore(new CategoryCreationResult(
            null,
            CategoryCreationOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store)
            .HandleAsync(new CreateCategoryCommand { Name = "Dining" });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenCreated_ThenNothingIsStored()
    {
        var store = new StubCategoryStore(new CategoryCreationResult(
            null,
            CategoryCreationOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(new()
        {
            Name = string.Empty,
            ParentId = Guid.Empty
        });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.NameRequired, result.Errors);
        Assert.Contains(CategoryMessages.ParentIdInvalid, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenCreated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubCategoryStore(new CategoryCreationResult(
            new CategorySnapshot(
                Guid.NewGuid(), userId, "Dining", null, false, Now, Now),
            CategoryCreationOutcome.Succeeded));
        var handler = new CreateCategoryCommandHandler(
            new CreateCategoryCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCategoryCommand { Name = "Dining" });

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static CreateCategoryCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        ICategoryStore store) => new(
            new CreateCategoryCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubCategoryStore(CategoryCreationResult result) : ICategoryStore
    {
        public CategoryCreation? Creation { get; private set; }

        public Task<CategoryCreationResult> CreateAsync(
            CategoryCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(result);
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
