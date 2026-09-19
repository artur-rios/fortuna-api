using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
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
    public async Task GivenNoPageParameters_WhenListed_ThenTheFirstDefaultPageIsReturned()
    {
        var profile = Profile();
        var store = new StubTagReader([
            new TagSnapshot(Guid.NewGuid(), "Food", false, Now, Now),
            new TagSnapshot(Guid.NewGuid(), "Travel", false, Now, Now)
        ]);

        var result = await Handler(profile, store).HandleAsync(new ListTagsQuery());

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Tags.Count);
        Assert.Equal(1, result.Data.PageNumber);
        Assert.Equal(100, result.Data.PageSize);
        Assert.Equal(2, result.Data.TotalItems);
        Assert.Equal(1, result.Data.TotalPages);
    }

    [UnitFact]
    public async Task GivenOversizedPage_WhenListed_ThenPageSizeIsCappedAndTotalsReported()
    {
        var profile = Profile();
        var store = new StubTagReader([
            new TagSnapshot(Guid.NewGuid(), "Food", false, Now, Now),
            new TagSnapshot(Guid.NewGuid(), "Travel", false, Now, Now),
            new TagSnapshot(Guid.NewGuid(), "Work", false, Now, Now)
        ]);

        var result = await Handler(profile, store, maximumPageSize: 2).HandleAsync(
            new ListTagsQuery { PageNumber = 2, PageSize = 500 });

        Assert.Equal(new PageRequest(2, 2), store.Page);
        Assert.Equal("Work", Assert.Single(result.Data!.Tags).Name);
        Assert.Equal(3, result.Data.TotalItems);
        Assert.Equal(2, result.Data.TotalPages);
    }

    [UnitFact]
    public async Task GivenInvalidPage_WhenListed_ThenValidationFailsWithoutReading()
    {
        var store = new StubTagReader([]);

        var result = await Handler(Profile(), store).HandleAsync(
            new ListTagsQuery { PageNumber = 0, PageSize = 0 });

        Assert.Contains(TagMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(TagMessages.InvalidPageSize, result.Errors);
        Assert.Null(store.UserId);
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

    private static IQueryHandlerAsync<ListTagsQuery, TagListOutput> Handler(
        UserProfileSnapshot? profile,
        ITagReader store,
        int maximumPageSize = 100) => new ListTagsQueryHandler(
        new CurrentProfileResolver(new StubActorAccessor(new RequestActor(
            profile?.ExternalSubject ?? Guid.NewGuid(),
            3,
            null,
            [])), new StubProfileReader(profile)),
        store,
        new PaginationOptions(maximumPageSize)).Validated(new ListTagsQueryValidator());

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

        public PageRequest? Page { get; private set; }

        public Task<ReadPage<TagSnapshot>> ListAsync(
            Guid userId,
            bool includeDeleted,
            PageRequest page,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            Page = page;

            return Task.FromResult(new ReadPage<TagSnapshot>(
                tags.Skip(page.Skip).Take(page.PageSize).ToArray(),
                tags.Count));
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
