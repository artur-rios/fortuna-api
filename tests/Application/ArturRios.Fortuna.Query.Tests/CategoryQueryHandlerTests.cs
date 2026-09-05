using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CategoryQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 19, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenCategoryHierarchy_WhenTreeRequested_ThenChildrenAndUsageAreAggregated()
    {
        var userId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();
        var reader = new StubCategoryReader([
            Category(rootId, "Living", null, 2),
            Category(grandchildId, "Restaurants", childId, 5),
            Category(childId, "Dining", rootId, 3)
        ]);
        var handler = TreeHandler(userId, reader);

        var result = await handler.HandleAsync(new GetCategoryTreeQuery
        {
            IncludeUsageCounts = true
        });

        Assert.True(result.Success);
        var root = Assert.Single(result.Data!.Categories);
        Assert.Equal(rootId, root.Id);
        Assert.Equal(10, root.UsageCount);
        var child = Assert.Single(root.Children);
        Assert.Equal(childId, child.Id);
        Assert.Equal(8, child.UsageCount);
        Assert.Equal(5, Assert.Single(child.Children).UsageCount);
        Assert.True(reader.IncludeUsageCounts);
        Assert.False(reader.IncludeDeleted);
    }

    [UnitFact]
    public async Task GivenNoCategories_WhenTreeRequested_ThenDefaultSetIsOffered()
    {
        var result = await TreeHandler(Guid.NewGuid(), new StubCategoryReader([]))
            .HandleAsync(new GetCategoryTreeQuery());

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Categories);
        Assert.True(result.Data.CanSeedDefaults);
        Assert.Contains(CategoryMessages.DefaultSetAvailable, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedRecordsRequested_WhenTreeRequested_ThenReaderReceivesOption()
    {
        var reader = new StubCategoryReader([]);

        await TreeHandler(Guid.NewGuid(), reader).HandleAsync(new GetCategoryTreeQuery
        {
            IncludeDeleted = true
        });

        Assert.True(reader.IncludeDeleted);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenTreeRequested_ThenNotFoundIsReturned()
    {
        var reader = new StubCategoryReader([]);
        var handler = new GetCategoryTreeQueryHandler(
            new StubUserProfileReader(null),
            reader,
            new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])));

        var result = await handler.HandleAsync(new GetCategoryTreeQuery());

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.False(reader.Called);
    }

    [UnitFact]
    public async Task GivenExistingCategory_WhenRequestedById_ThenItsSubtreeIsReturned()
    {
        var userId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var reader = new StubCategoryReader([
            Category(rootId, "Root", null, 1),
            Category(childId, "Child", rootId, 2)
        ]);
        var handler = ByIdHandler(userId, reader);

        var result = await handler.HandleAsync(new GetCategoryByIdQuery
        {
            Id = rootId,
            IncludeUsageCounts = true
        });

        Assert.True(result.Success);
        Assert.Equal(rootId, result.Data!.Id);
        Assert.Equal(3, result.Data.UsageCount);
        Assert.Equal(childId, Assert.Single(result.Data.Children).Id);
        Assert.Contains(CategoryMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenUnknownCategory_WhenRequestedById_ThenNotFoundIsReturned()
    {
        var handler = ByIdHandler(Guid.NewGuid(), new StubCategoryReader([]));

        var result = await handler.HandleAsync(new GetCategoryByIdQuery { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCategoryRequestedById_ThenNotFoundIsReturned()
    {
        var reader = new StubCategoryReader([]);
        var handler = new GetCategoryByIdQueryHandler(
            new StubUserProfileReader(null),
            reader,
            new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])));

        var result = await handler.HandleAsync(new GetCategoryByIdQuery { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.False(reader.Called);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenTreeRequested_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId));
        var handler = new GetCategoryTreeQueryHandler(
            profiles,
            new StubCategoryReader([]),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }));

        var result = await handler.HandleAsync(new GetCategoryTreeQuery());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static GetCategoryTreeQueryHandler TreeHandler(
        Guid userId,
        ICategoryReader reader) => new(
            new StubUserProfileReader(Profile(userId)),
            reader,
            new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])));

    private static GetCategoryByIdQueryHandler ByIdHandler(
        Guid userId,
        ICategoryReader reader) => new(
            new StubUserProfileReader(Profile(userId)),
            reader,
            new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])));

    private static UserProfileSnapshot Profile(Guid id) => new(
        id,
        Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private static CategoryReadSnapshot Category(
        Guid id,
        string name,
        Guid? parentId,
        int directUsageCount) => new(
            id,
            name,
            parentId,
            false,
            directUsageCount,
            Now,
            Now);

    private sealed class StubCategoryReader(IReadOnlyCollection<CategoryReadSnapshot> categories)
        : ICategoryReader
    {
        public bool Called { get; private set; }
        public bool IncludeDeleted { get; private set; }
        public bool IncludeUsageCounts { get; private set; }

        public Task<IReadOnlyCollection<CategoryReadSnapshot>> ListAsync(
            Guid userId,
            bool includeDeleted,
            bool includeUsageCounts,
            CancellationToken cancellationToken)
        {
            Called = true;
            IncludeDeleted = includeDeleted;
            IncludeUsageCounts = includeUsageCounts;
            return Task.FromResult(categories);
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
}
