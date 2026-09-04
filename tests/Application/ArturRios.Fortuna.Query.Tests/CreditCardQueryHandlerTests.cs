using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class CreditCardQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 18, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOverLimitCard_WhenReadById_ThenAvailableIsZeroAndOverageIsPositive()
    {
        var profile = Profile();
        var card = Card(profile.Id, creditLimit: 1000m, outstandingAmount: 1250.50m);

        var result = await GetHandler(profile, new StubCreditCardReader(card)).HandleAsync(
            new GetCreditCardByIdQuery { Id = card.Id });

        Assert.True(result.Success);
        Assert.Equal(card.Id, result.Data?.Id);
        Assert.Equal(1250.50m, result.Data?.UsedAmount);
        Assert.Equal(0m, result.Data?.AvailableAmount);
        Assert.Equal(250.50m, result.Data?.OverageAmount);
        Assert.Contains(CreditCardMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenCardWithoutCharges_WhenReadById_ThenFullLimitIsAvailable()
    {
        var profile = Profile();
        var card = Card(profile.Id, creditLimit: 2000m);

        var result = await GetHandler(profile, new StubCreditCardReader(card)).HandleAsync(
            new GetCreditCardByIdQuery { Id = card.Id });

        Assert.Equal(0m, result.Data?.UsedAmount);
        Assert.Equal(2000m, result.Data?.AvailableAmount);
        Assert.Equal(0m, result.Data?.OverageAmount);
    }

    [UnitFact]
    public async Task GivenCreditBalance_WhenReadById_ThenUsedAmountDoesNotBecomeNegative()
    {
        var profile = Profile();
        var card = Card(profile.Id, creditLimit: 1000m, outstandingAmount: -50m);

        var result = await GetHandler(profile, new StubCreditCardReader(card)).HandleAsync(
            new GetCreditCardByIdQuery { Id = card.Id });

        Assert.Equal(0m, result.Data?.UsedAmount);
        Assert.Equal(1000m, result.Data?.AvailableAmount);
        Assert.Equal(0m, result.Data?.OverageAmount);
    }

    [UnitFact]
    public async Task GivenMissingForeignOrDeletedCard_WhenReadById_ThenSameNotFoundIsReturned()
    {
        var actor = Profile();
        var foreign = Card(Guid.NewGuid());
        var deleted = Card(actor.Id, isDeleted: true);
        var reader = new StubCreditCardReader(foreign, deleted);
        var handler = GetHandler(actor, reader);

        var foreignResult = await handler.HandleAsync(new GetCreditCardByIdQuery { Id = foreign.Id });
        var deletedResult = await handler.HandleAsync(new GetCreditCardByIdQuery { Id = deleted.Id });
        var missingResult = await handler.HandleAsync(new GetCreditCardByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(CreditCardMessages.NotFound, foreignResult.Errors);
        Assert.Equal(foreignResult.Errors, deletedResult.Errors);
        Assert.Equal(foreignResult.Errors, missingResult.Errors);
    }

    [UnitFact]
    public async Task GivenFiltersAndSort_WhenListed_ThenOnlyOwnedLiveMatchesAreReturned()
    {
        var actor = Profile();
        var first = Card(actor.Id, "Reserve One", "Example Bank", "USD", 1000m, 100m);
        var second = Card(actor.Id, "Reserve Two", "Example Bank", "USD", 2000m, 500m);
        var wrongIssuer = Card(actor.Id, "Reserve Other", "Other Bank", "USD", 3000m, 900m);
        var foreign = Card(Guid.NewGuid(), "Reserve Foreign", "Example Bank", "USD", 4000m, 700m);
        var deleted = Card(actor.Id, "Reserve Deleted", "Example Bank", "USD", 5000m, 800m, true);
        var handler = ListHandler(
            actor,
            new StubCreditCardReader(first, second, wrongIssuer, foreign, deleted));

        var result = await handler.HandleAsync(new ListCreditCardsQuery
        {
            Name = " reserve ",
            Issuer = "AMPLE",
            CurrencyCode = "usd",
            SortBy = "UsedAmount",
            Descending = true,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal([500m, 100m], result.Data!.Select(card => card.UsedAmount));
        Assert.Contains(CreditCardMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenFieldsAreNamedInErrors()
    {
        var result = await ListHandler(null, new StubCreditCardReader()).HandleAsync(
            new ListCreditCardsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                Name = new string('n', 201),
                Issuer = new string('i', 201),
                CurrencyCode = "US",
                SortBy = "AvailableAmount"
            });

        Assert.False(result.Success);
        Assert.Contains(CreditCardMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(CreditCardMessages.InvalidPageSize, result.Errors);
        Assert.Contains(CreditCardMessages.NameTooLong, result.Errors);
        Assert.Contains(CreditCardMessages.IssuerTooLong, result.Errors);
        Assert.Contains(CreditCardMessages.CurrencyInvalid, result.Errors);
        Assert.Contains(CreditCardMessages.SortByUnsupported, result.Errors);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenListed_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var profiles = new StubUserProfileReader(profile);
        var handler = new ListCreditCardsQueryHandler(
            new ListCreditCardsQueryValidator(),
            profiles,
            new StubCreditCardReader(Card(profile.Id)),
            new StubActorAccessor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            new PaginationOptions(100));

        var result = await handler.HandleAsync(new ListCreditCardsQuery());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
        Assert.Single(result.Data!);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenReadOrListed_ThenNotFoundIsReturned()
    {
        var reader = new StubCreditCardReader();

        var detail = await GetHandler(null, reader).HandleAsync(new GetCreditCardByIdQuery());
        var list = await ListHandler(null, reader).HandleAsync(new ListCreditCardsQuery());

        Assert.Contains(CreditCardMessages.ProfileNotFound, detail.Errors);
        Assert.Contains(CreditCardMessages.ProfileNotFound, list.Errors);
    }

    private static GetCreditCardByIdQueryHandler GetHandler(
        UserProfileSnapshot? profile,
        ICreditCardReader cards) => new(
            new StubUserProfileReader(profile),
            cards,
            Actor(profile));

    private static ListCreditCardsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        ICreditCardReader cards,
        int maximumPageSize = 100) => new(
            new ListCreditCardsQueryValidator(),
            new StubUserProfileReader(profile),
            cards,
            Actor(profile),
            new PaginationOptions(maximumPageSize));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile(Guid? externalSubject = null) => new(
        Guid.NewGuid(),
        externalSubject ?? Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private static CreditCardLimitSnapshot Card(
        Guid userId,
        string name = "Rewards",
        string issuer = "Example Bank",
        string currencyCode = "BRL",
        decimal creditLimit = 1000m,
        decimal outstandingAmount = 0m,
        bool isDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Issuer = issuer,
            CurrencyCode = currencyCode,
            CreditLimit = creditLimit,
            ClosingDay = 20,
            DueDay = 5,
            LastFourDigits = "1234",
            OutstandingAmount = outstandingAmount,
            IsDeleted = isDeleted,
            CreatedAt = Now,
            UpdatedAt = Now
        };

    private sealed class StubCreditCardReader(params CreditCardLimitSnapshot[] cards)
        : ICreditCardReader
    {
        public IQueryable<CreditCardLimitSnapshot> QueryLimits() => cards.AsQueryable();

        public Task<CreditCardLimitSnapshot?> FindByIdWithLimitsAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) => Task.FromResult(cards.SingleOrDefault(card =>
                card.UserId == userId && card.Id == id && !card.IsDeleted));
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
