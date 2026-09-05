using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class UpdateCategoryCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 22, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidDetails_WhenUpdated_ThenNormalizedCategoryIsReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var createdAt = Now.AddDays(-1);
        var store = new StubCategoryUpdater(new CategoryUpdateResult(
            new CategorySnapshot(
                categoryId, userId, "Dining", parentId, false, createdAt, Now),
            CategoryUpdateOutcome.Succeeded));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(new()
        {
            Id = categoryId,
            Name = "  Dining  ",
            ParentId = parentId
        });

        Assert.True(result.Success);
        Assert.Equal(categoryId, result.Data!.Id);
        Assert.Equal("Dining", result.Data.Name);
        Assert.Equal(parentId, result.Data.ParentId);
        Assert.Equal(createdAt, result.Data.CreatedAt);
        Assert.Equal(Now, result.Data.UpdatedAt);
        Assert.Equal(userId, store.Update!.UserId);
        Assert.Equal(categoryId, store.Update.Id);
        Assert.Equal("Dining", store.Update.Name);
        Assert.Equal(parentId, store.Update.ParentId);
        Assert.Equal(Now, store.Update.UpdatedAt);
        Assert.Contains(CategoryMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CategoryUpdateOutcome.NotFound, CategoryMessages.NotFound)]
    [InlineData(CategoryUpdateOutcome.ParentNotFound, CategoryMessages.ParentNotFound)]
    [InlineData(CategoryUpdateOutcome.DuplicateSiblingName, CategoryMessages.DuplicateSiblingName)]
    [InlineData(CategoryUpdateOutcome.CycleDetected, CategoryMessages.CycleDetected)]
    public async Task GivenStoreRefusal_WhenUpdated_ThenExpectedErrorIsReturned(
        CategoryUpdateOutcome outcome,
        string expected)
    {
        var subject = Guid.NewGuid();
        var store = new StubCategoryUpdater(new CategoryUpdateResult(null, outcome));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenNotFoundIsReturned()
    {
        var store = new StubCategoryUpdater(new CategoryUpdateResult(
            null,
            CategoryUpdateOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenUpdated_ThenNothingIsStored()
    {
        var store = new StubCategoryUpdater(new CategoryUpdateResult(
            null,
            CategoryUpdateOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(new()
        {
            Id = Guid.Empty,
            Name = string.Empty,
            ParentId = Guid.Empty
        });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.NotFound, result.Errors);
        Assert.Contains(CategoryMessages.NameRequired, result.Errors);
        Assert.Contains(CategoryMessages.ParentIdInvalid, result.Errors);
        Assert.Null(store.Update);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenUpdated_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var command = ValidCommand();
        var store = new StubCategoryUpdater(new CategoryUpdateResult(
            new CategorySnapshot(
                command.Id, userId, command.Name, null, false, Now, Now),
            CategoryUpdateOutcome.Succeeded));
        var handler = new UpdateCategoryCommandHandler(
            new UpdateCategoryCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(command);

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static UpdateCategoryCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        ICategoryUpdater store) => new(
            new UpdateCategoryCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

    private static UpdateCategoryCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Dining"
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubCategoryUpdater(CategoryUpdateResult result) : ICategoryUpdater
    {
        public CategoryUpdate? Update { get; private set; }

        public Task<CategoryUpdateResult> UpdateAsync(
            CategoryUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
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
