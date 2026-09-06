using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CounterpartyQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 2, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedCounterparties_WhenListed_ThenSnapshotsAreReturned()
    {
        var profile = Profile();
        var store = new StubCounterpartyReader([
            new CounterpartySnapshot(Guid.NewGuid(), "Shop", false, Now, Now)
        ]);
        var handler = new ListCounterpartiesQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store);

        var result = await handler.HandleAsync(new ListCounterpartiesQuery
        {
            IncludeDeleted = true
        });

        Assert.True(result.Success);
        Assert.Equal("Shop", Assert.Single(result.Data!.Counterparties).Name);
        Assert.Equal(profile.Id, store.UserId);
        Assert.True(store.IncludeDeleted);
        Assert.Contains(CounterpartyMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenRecentCategory_WhenSuggested_ThenCategoryIsReturned()
    {
        var profile = Profile();
        var counterpartyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var store = new StubCounterpartySuggester(new CounterpartyCategorySuggestionResult(
            counterpartyId,
            categoryId,
            "Dining",
            CounterpartyCategorySuggestionOutcome.Succeeded));
        var handler = new SuggestCounterpartyCategoryQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store);

        var result = await handler.HandleAsync(new SuggestCounterpartyCategoryQuery
        {
            Id = counterpartyId
        });

        Assert.True(result.Success);
        Assert.True(result.Data?.HasSuggestion);
        Assert.Equal(categoryId, result.Data?.CategoryId);
        Assert.Equal(profile.Id, store.UserId);
        Assert.Contains(CounterpartyMessages.SuggestedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoHistory_WhenSuggested_ThenEmptySuggestionIsExplained()
    {
        var counterpartyId = Guid.NewGuid();
        var profile = Profile();
        var store = new StubCounterpartySuggester(new CounterpartyCategorySuggestionResult(
            counterpartyId,
            null,
            null,
            CounterpartyCategorySuggestionOutcome.Succeeded));
        var handler = new SuggestCounterpartyCategoryQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store);

        var result = await handler.HandleAsync(new SuggestCounterpartyCategoryQuery
        {
            Id = counterpartyId
        });

        Assert.True(result.Success);
        Assert.False(result.Data?.HasSuggestion);
        Assert.Null(result.Data?.CategoryId);
        Assert.Contains(CounterpartyMessages.NoSuggestion, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingCounterparty_WhenSuggested_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var store = new StubCounterpartySuggester(new CounterpartyCategorySuggestionResult(
            null,
            null,
            null,
            CounterpartyCategorySuggestionOutcome.NotFound));
        var handler = new SuggestCounterpartyCategoryQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store);

        var result = await handler.HandleAsync(new SuggestCounterpartyCategoryQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenListed_ThenStoreIsNotCalled()
    {
        var store = new StubCounterpartyReader([]);
        var handler = new ListCounterpartiesQueryHandler(
            Actor(null),
            new StubProfileReader(null),
            store);

        var result = await handler.HandleAsync(new ListCounterpartiesQuery());

        Assert.False(result.Success);
        Assert.Contains(CounterpartyMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.UserId);
    }

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubCounterpartyReader(
        IReadOnlyCollection<CounterpartySnapshot> counterparties) : ICounterpartyReader
    {
        public Guid? UserId { get; private set; }
        public bool IncludeDeleted { get; private set; }

        public Task<IReadOnlyCollection<CounterpartySnapshot>> ListAsync(
            Guid userId,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            return Task.FromResult(counterparties);
        }
    }

    private sealed class StubCounterpartySuggester(
        CounterpartyCategorySuggestionResult result) : ICounterpartyCategorySuggester
    {
        public Guid? UserId { get; private set; }

        public Task<CounterpartyCategorySuggestionResult> SuggestCategoryAsync(
            Guid userId,
            Guid counterpartyId,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            return Task.FromResult(result);
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
