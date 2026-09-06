using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ListTagsQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 1, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTags_WhenListed_ThenSnapshotsAreReturned()
    {
        var profile = Profile();
        var store = new StubTagReader([
            new TagSnapshot(Guid.NewGuid(), "Food", false, Now, Now)
        ]);
        var handler = Handler(profile, store);

        var result = await handler.HandleAsync(new ListTagsQuery { IncludeDeleted = true });

        Assert.True(result.Success);
        Assert.Equal("Food", Assert.Single(result.Data!.Tags).Name);
        Assert.Equal(profile.Id, store.UserId);
        Assert.True(store.IncludeDeleted);
        Assert.Contains(TagMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenListed_ThenStoreIsNotCalled()
    {
        var store = new StubTagReader([]);

        var result = await Handler(null, store).HandleAsync(new ListTagsQuery());

        Assert.False(result.Success);
        Assert.Contains(TagMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    private static ListTagsQueryHandler Handler(
        UserProfileSnapshot? profile,
        ITagReader store) => new(
        new StubActorAccessor(new RequestActor(
            profile?.ExternalSubject ?? Guid.NewGuid(),
            3,
            null,
            [])),
        new StubProfileReader(profile),
        store);

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubTagReader(IReadOnlyCollection<TagSnapshot> tags) : ITagReader
    {
        public Guid? UserId { get; private set; }
        public bool IncludeDeleted { get; private set; }

        public Task<IReadOnlyCollection<TagSnapshot>> ListAsync(
            Guid userId,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            return Task.FromResult(tags);
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
}
