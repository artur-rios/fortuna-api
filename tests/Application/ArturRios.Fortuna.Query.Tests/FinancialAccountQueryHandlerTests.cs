using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class FinancialAccountQueryHandlerTests
{
    [UnitFact]
    public async Task GivenOwnedAccount_WhenReadById_ThenAccountIsReturned()
    {
        var user = User();
        var account = Account(user, "Daily", "Example Bank", FinancialAccountType.Checking, "BRL", -10);
        var handler = GetHandler(Profile(user), new StubFinancialAccountReader(account));

        var result = await handler.HandleAsync(new GetFinancialAccountByIdQuery
        {
            Id = account.PublicId
        });

        Assert.True(result.Success);
        Assert.Equal(account.PublicId, result.Data?.Id);
        Assert.Equal("Daily", result.Data?.Name);
        Assert.Equal("Example Bank", result.Data?.Institution);
        Assert.Equal(FinancialAccountType.Checking, result.Data?.AccountType);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(-10, result.Data?.OpeningBalance);
        Assert.False(result.Data?.IsDeleted);
        Assert.Contains(FinancialAccountMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingOrForeignAccount_WhenReadById_ThenSameNotFoundIsReturned()
    {
        var owner = User();
        var actor = User();
        var account = Account(owner, "Private");
        var handler = GetHandler(Profile(actor), new StubFinancialAccountReader(account));

        var foreign = await handler.HandleAsync(new GetFinancialAccountByIdQuery
        {
            Id = account.PublicId
        });
        var missing = await handler.HandleAsync(new GetFinancialAccountByIdQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.False(foreign.Success);
        Assert.False(missing.Success);
        Assert.Equal(foreign.Errors, missing.Errors);
        Assert.Contains(FinancialAccountMessages.NotFound, foreign.Errors);
    }

    [UnitFact]
    public async Task GivenDeletedAccount_WhenReadById_ThenExplicitInclusionControlsVisibility()
    {
        var user = User();
        var account = Account(user, "Archived");
        account.SoftDelete(DateTimeOffset.UtcNow);
        var handler = GetHandler(Profile(user), new StubFinancialAccountReader(account));

        var hidden = await handler.HandleAsync(new GetFinancialAccountByIdQuery
        {
            Id = account.PublicId
        });
        var included = await handler.HandleAsync(new GetFinancialAccountByIdQuery
        {
            Id = account.PublicId,
            IncludeDeleted = true
        });

        Assert.False(hidden.Success);
        Assert.True(included.Success);
        Assert.True(included.Data?.IsDeleted);
    }

    [UnitFact]
    public async Task GivenAccountFilters_WhenListed_ThenOnlyOwnedMatchingAccountsAreSorted()
    {
        var actor = User();
        var other = User();
        var matchingFirst = Account(
            actor,
            "House Reserve",
            "Example Bank",
            FinancialAccountType.Savings,
            "USD",
            10);
        var matchingSecond = Account(
            actor,
            "Household",
            "Example Bank",
            FinancialAccountType.Savings,
            "USD",
            20);
        var wrongType = Account(actor, "House Cash", "Example Bank", FinancialAccountType.Cash, "USD", 30);
        var foreign = Account(other, "House Foreign", "Example Bank", FinancialAccountType.Savings, "USD", 40);
        var deleted = Account(actor, "House Deleted", "Example Bank", FinancialAccountType.Savings, "USD", 50);
        deleted.SoftDelete(DateTimeOffset.UtcNow);
        var handler = ListHandler(
            Profile(actor),
            new StubFinancialAccountReader(matchingFirst, matchingSecond, wrongType, foreign, deleted));

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery
        {
            Name = " house ",
            Institution = "AMPLE",
            AccountType = FinancialAccountType.Savings,
            CurrencyCode = "usd",
            SortBy = "OpeningBalance",
            Descending = true,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal([20m, 10m], result.Data!.Select(item => item.OpeningBalance));
        Assert.All(result.Data!, item => Assert.False(item.IsDeleted));
        Assert.Contains(FinancialAccountMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedAccount_WhenListedExplicitly_ThenItIsIncluded()
    {
        var user = User();
        var live = Account(user, "Live");
        var deleted = Account(user, "Deleted");
        deleted.SoftDelete(DateTimeOffset.UtcNow);
        var handler = ListHandler(Profile(user), new StubFinancialAccountReader(live, deleted));

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery
        {
            IncludeDeleted = true
        });

        Assert.Equal(2, result.TotalItems);
        Assert.Single(result.Data!, item => item.IsDeleted);
    }

    [UnitTheory]
    [InlineData("Name", false, "Alpha")]
    [InlineData("Name", true, "Beta")]
    [InlineData("Institution", false, "Beta")]
    [InlineData("Institution", true, "Alpha")]
    [InlineData("AccountType", false, "Alpha")]
    [InlineData("AccountType", true, "Beta")]
    [InlineData("CurrencyCode", false, "Beta")]
    [InlineData("CurrencyCode", true, "Alpha")]
    [InlineData("OpeningBalance", false, "Alpha")]
    [InlineData("OpeningBalance", true, "Beta")]
    [InlineData("CreatedAt", false, "Beta")]
    [InlineData("CreatedAt", true, "Alpha")]
    [InlineData("UpdatedAt", false, "Beta")]
    [InlineData("UpdatedAt", true, "Alpha")]
    public async Task GivenSupportedSort_WhenListed_ThenRequestedOrderingIsApplied(
        string sortBy,
        bool descending,
        string expectedFirst)
    {
        var user = User();
        var first = Account(
            user,
            "Alpha",
            "Z Bank",
            FinancialAccountType.Checking,
            "USD",
            10,
            new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        var second = Account(
            user,
            "Beta",
            "A Bank",
            FinancialAccountType.Savings,
            "BRL",
            20,
            new DateTimeOffset(2026, 9, 4, 11, 0, 0, TimeSpan.Zero));
        var handler = ListHandler(Profile(user), new StubFinancialAccountReader(first, second));

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery
        {
            SortBy = sortBy,
            Descending = descending
        });

        Assert.Equal(expectedFirst, result.Data?.First().Name);
    }

    [UnitFact]
    public async Task GivenOversizedPage_WhenListed_ThenConfiguredMaximumIsReported()
    {
        var user = User();
        var handler = ListHandler(
            Profile(user),
            new StubFinancialAccountReader(Account(user, "One")),
            maximumPageSize: 25);

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery
        {
            PageSize = 500
        });

        Assert.True(result.Success);
        Assert.Equal(25, result.PageSize);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenFieldsAreNamedInErrors()
    {
        var handler = ListHandler(null, new StubFinancialAccountReader());

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery
        {
            PageNumber = 0,
            PageSize = 0,
            Name = new string('n', 201),
            Institution = new string('i', 201),
            AccountType = (FinancialAccountType)999,
            CurrencyCode = "US",
            SortBy = "Balance"
        });

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(FinancialAccountMessages.InvalidPageSize, result.Errors);
        Assert.Contains(FinancialAccountMessages.NameTooLong, result.Errors);
        Assert.Contains(FinancialAccountMessages.InstitutionTooLong, result.Errors);
        Assert.Contains(FinancialAccountMessages.AccountTypeInvalid, result.Errors);
        Assert.Contains(FinancialAccountMessages.CurrencyInvalid, result.Errors);
        Assert.Contains(FinancialAccountMessages.SortByUnsupported, result.Errors);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenListed_ThenProfileIsResolvedByPublicId()
    {
        var user = User();
        var profile = Profile(user, externalSubject: null);
        var profiles = new StubUserProfileReader(profile);
        var handler = new ListFinancialAccountsQueryHandler(
            new ListFinancialAccountsQueryValidator(),
            profiles,
            new StubFinancialAccountReader(Account(user, "Local")),
            new StubRequestActorAccessor(new RequestActor(profile.Id, 3, null, [])
            {
                IsLocal = true
            }),
            new PaginationOptions(100));

        var result = await handler.HandleAsync(new ListFinancialAccountsQuery());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
        Assert.Single(result.Data!);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenReadOrListed_ThenNotFoundIsReturned()
    {
        var reader = new StubFinancialAccountReader();

        var detail = await GetHandler(null, reader).HandleAsync(new GetFinancialAccountByIdQuery());
        var list = await ListHandler(null, reader).HandleAsync(new ListFinancialAccountsQuery());

        Assert.Contains(FinancialAccountMessages.ProfileNotFound, detail.Errors);
        Assert.Contains(FinancialAccountMessages.ProfileNotFound, list.Errors);
    }

    private static GetFinancialAccountByIdQueryHandler GetHandler(
        UserProfileSnapshot? profile,
        IFinancialAccountReader accounts) => new(
        new StubUserProfileReader(profile),
        accounts,
        Actor(profile));

    private static ListFinancialAccountsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        IFinancialAccountReader accounts,
        int maximumPageSize = 100) => new(
        new ListFinancialAccountsQueryValidator(),
        new StubUserProfileReader(profile),
        accounts,
        Actor(profile),
        new PaginationOptions(maximumPageSize));

    private static StubRequestActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static Currency Currency(string code = "BRL") => new(code, $"{code} currency", 2);

    private static UserProfile User() => new(
        Guid.NewGuid(),
        "Account Owner",
        Currency(),
        DateTimeOffset.UtcNow);

    private static FinancialAccount Account(
        UserProfile user,
        string name,
        string? institution = null,
        FinancialAccountType accountType = FinancialAccountType.Checking,
        string currencyCode = "BRL",
        decimal openingBalance = 0,
        DateTimeOffset? createdAt = null) => new(
        user,
        name,
        institution,
        accountType,
        Currency(currencyCode),
        openingBalance,
        createdAt ?? DateTimeOffset.UtcNow);

    private static UserProfileSnapshot Profile(UserProfile user, Guid? externalSubject = null) => new(
        user.PublicId,
        externalSubject ?? Guid.Parse(user.ExternalSubject!),
        user.DisplayName,
        user.DisplayCurrency.Code,
        user.IsDeleted,
        user.CreatedAt,
        user.UpdatedAt);

    private sealed class StubFinancialAccountReader(params FinancialAccount[] accounts)
        : IFinancialAccountReader
    {
        public IQueryable<FinancialAccount> Query() => accounts.AsQueryable();

        public Task<FinancialAccountSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken)
        {
            var account = accounts.SingleOrDefault(item =>
                item.User.PublicId == userId &&
                item.PublicId == id &&
                (includeDeleted || !item.IsDeleted));
            return Task.FromResult(account is null ? null : Snapshot(account));
        }

        private static FinancialAccountSnapshot Snapshot(FinancialAccount account) => new(
            account.PublicId,
            account.User.PublicId,
            account.Name,
            account.Institution,
            account.AccountType,
            account.Currency.Code,
            account.OpeningBalance,
            account.IsDeleted,
            account.CreatedAt,
            account.UpdatedAt);
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

    private sealed class StubRequestActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
