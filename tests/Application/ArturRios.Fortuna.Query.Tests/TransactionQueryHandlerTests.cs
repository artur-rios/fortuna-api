using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class TransactionQueryHandlerTests
{
    private static readonly Guid ExternalSubject =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTransaction_WhenDetailIsRead_ThenCompleteRowIsReturned()
    {
        var profile = Profile();
        var transaction = Snapshot(profile.Id, "Purchase", "USD", 12.34m);

        var result = await DetailHandler(
            profile,
            new StubTransactionReader(transaction)).HandleAsync(new GetTransactionByIdQuery
            {
                Id = transaction.Id
            });

        Assert.True(result.Success);
        Assert.Equal(transaction.Id, result.Data?.Id);
        Assert.Equal(transaction.FinancialAccountName, result.Data?.FinancialAccountName);
        Assert.Equal(transaction.CategoryName, result.Data?.CategoryName);
        Assert.Equal(transaction.CounterpartyName, result.Data?.CounterpartyName);
        Assert.Equal(transaction.Tags.Single().Name, result.Data?.Tags.Single().Name);
        Assert.Equal(transaction.SourceType, result.Data?.SourceType);
        Assert.Contains(TransactionMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDeletedTransactionWithoutOptIn_WhenDetailIsRead_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var transaction = Snapshot(profile.Id, "Deleted", isDeleted: true);

        var result = await DetailHandler(
            profile,
            new StubTransactionReader(transaction)).HandleAsync(new GetTransactionByIdQuery
            {
                Id = transaction.Id
            });

        Assert.Contains(TransactionMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenDeletedTransactionWithOptIn_WhenDetailIsRead_ThenDeletedRowIsReturned()
    {
        var profile = Profile();
        var transaction = Snapshot(profile.Id, "Deleted", isDeleted: true);

        var result = await DetailHandler(
            profile,
            new StubTransactionReader(transaction)).HandleAsync(new GetTransactionByIdQuery
            {
                Id = transaction.Id,
                IncludeDeleted = true
            });

        Assert.True(result.Data?.IsDeleted);
    }

    [UnitFact]
    public async Task GivenForeignTransaction_WhenDetailIsRead_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var transaction = Snapshot(Guid.NewGuid(), "Foreign");

        var result = await DetailHandler(
            profile,
            new StubTransactionReader(transaction)).HandleAsync(new GetTransactionByIdQuery
            {
                Id = transaction.Id,
                IncludeDeleted = true
            });

        Assert.Contains(TransactionMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenTransactionIsRead_ThenProfileNotFoundIsReturned()
    {
        var result = await DetailHandler(
            null,
            new StubTransactionReader()).HandleAsync(new GetTransactionByIdQuery
            {
                Id = Guid.NewGuid()
            });

        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidId_WhenDetailIsRead_ThenValidationErrorIsReturned()
    {
        var result = await DetailHandler(
            Profile(),
            new StubTransactionReader()).HandleAsync(new GetTransactionByIdQuery());

        Assert.Contains(TransactionMessages.TransactionIdRequired, result.Errors);
    }

    [UnitFact]
    public async Task GivenNoCriteria_WhenTransactionsAreSearched_ThenRecentPageIsReturned()
    {
        var profile = Profile();
        var older = Snapshot(profile.Id, "Older", occurredOn: new DateOnly(2026, 9, 4));
        var newer = Snapshot(profile.Id, "Newer", occurredOn: new DateOnly(2026, 9, 5));

        var result = await SearchHandler(
            profile,
            new StubTransactionReader(older, newer)).HandleAsync(new SearchTransactionsQuery());

        Assert.True(result.Success);
        Assert.Equal(newer.Id, result.Data?.Items.First().Id);
        Assert.Equal(2, result.Data?.TotalItems);
        Assert.Contains(TransactionMessages.ListedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData("OccurredOn", false, "Alpha")]
    [InlineData("OccurredOn", true, "Zulu")]
    [InlineData("Amount", false, "Zulu")]
    [InlineData("Amount", true, "Alpha")]
    [InlineData("Direction", false, "Zulu")]
    [InlineData("Direction", true, "Alpha")]
    [InlineData("Category", false, "Zulu")]
    [InlineData("Category", true, "Alpha")]
    [InlineData("Counterparty", false, "Alpha")]
    [InlineData("Counterparty", true, "Zulu")]
    [InlineData("CurrencyCode", false, "Alpha")]
    [InlineData("CurrencyCode", true, "Zulu")]
    [InlineData("Description", false, "Alpha")]
    [InlineData("Description", true, "Zulu")]
    [InlineData("CreatedAt", false, "Alpha")]
    [InlineData("CreatedAt", true, "Zulu")]
    [InlineData("UpdatedAt", false, "Alpha")]
    [InlineData("UpdatedAt", true, "Zulu")]
    public async Task GivenSupportedSort_WhenTransactionsAreSearched_ThenOrderingIsApplied(
        string sortBy,
        bool descending,
        string expectedDescription)
    {
        var profile = Profile();
        var alpha = Snapshot(
            profile.Id,
            "Zulu",
            "USD",
            10m,
            TransactionDirection.Expense,
            "Alpha",
            "Zulu",
            new DateOnly(2026, 9, 5),
            Now);
        var beta = Snapshot(
            profile.Id,
            "Alpha",
            "BRL",
            20m,
            TransactionDirection.Earning,
            "Beta",
            "Alpha",
            new DateOnly(2026, 9, 4),
            Now.AddHours(-1));

        var result = await SearchHandler(
            profile,
            new StubTransactionReader(alpha, beta)).HandleAsync(new SearchTransactionsQuery
            {
                SortBy = sortBy,
                Descending = descending
            });

        Assert.Equal(expectedDescription, result.Data?.Items.First().Description);
    }

    [UnitFact]
    public async Task GivenAllCriteriaAndLargePage_WhenTransactionsAreSearched_ThenCriteriaAndLimitAreApplied()
    {
        var profile = Profile();
        var reader = new StubTransactionReader(Snapshot(profile.Id, "Match"));
        var query = new SearchTransactionsQuery
        {
            From = new DateOnly(2026, 9, 1),
            To = new DateOnly(2026, 9, 5),
            FinancialAccountId = Guid.NewGuid(),
            CreditCardId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            TagId = Guid.NewGuid(),
            CounterpartyId = Guid.NewGuid(),
            Direction = TransactionDirection.Expense,
            MinimumAmount = 1m,
            MaximumAmount = 2m,
            Text = "  coffee  ",
            IncludeDeleted = true,
            PageSize = 500
        };

        var result = await SearchHandler(profile, reader, maximumPageSize: 25)
            .HandleAsync(query);

        Assert.Equal(25, result.Data?.PageSize);
        Assert.Equal(query.From, reader.LastCriteria?.From);
        Assert.Equal(query.To, reader.LastCriteria?.To);
        Assert.Equal(query.FinancialAccountId, reader.LastCriteria?.FinancialAccountId);
        Assert.Equal(query.CreditCardId, reader.LastCriteria?.CreditCardId);
        Assert.Equal(query.CategoryId, reader.LastCriteria?.CategoryId);
        Assert.Equal(query.TagId, reader.LastCriteria?.TagId);
        Assert.Equal(query.CounterpartyId, reader.LastCriteria?.CounterpartyId);
        Assert.Equal(query.Direction, reader.LastCriteria?.Direction);
        Assert.Equal(query.MinimumAmount, reader.LastCriteria?.MinimumAmount);
        Assert.Equal(query.MaximumAmount, reader.LastCriteria?.MaximumAmount);
        Assert.Equal("coffee", reader.LastCriteria?.Text);
        Assert.True(reader.LastCriteria?.IncludeDeleted);
    }

    [UnitFact]
    public async Task GivenSeveralCurrenciesWithoutDisplayCurrency_WhenSearched_ThenTotalsRemainSplit()
    {
        var profile = Profile();
        var reader = new StubTransactionReader(
            [],
            [
                new("BRL", 20m, 5m),
                new("USD", 10m, 3m)
            ]);

        var result = await SearchHandler(profile, reader).HandleAsync(new SearchTransactionsQuery());

        Assert.Equal(2, result.Data?.Totals.ByCurrency.Count);
        Assert.Null(result.Data?.Totals.DisplayCurrencyCode);
        Assert.Null(result.Data?.Totals.DisplayNet);
        Assert.Equal(-15m, result.Data?.Totals.ByCurrency.First().Net);
    }

    [UnitFact]
    public async Task GivenDisplayCurrencyAndRates_WhenSearched_ThenTotalsAreConvertedAndRoundedOnce()
    {
        var profile = Profile();
        var reader = new StubTransactionReader(
            [],
            [
                new("BRL", 3.335m, 5.005m),
                new("USD", 10.005m, 2.001m)
            ]);
        var rate = new ExchangeRateSnapshot(
            "USD",
            "BRL",
            5m,
            new DateOnly(2026, 9, 4),
            ExchangeRateSource.Manual);
        var rates = new StubRateReader(rate);

        var result = await SearchHandler(profile, reader, rates: rates).HandleAsync(
            new SearchTransactionsQuery
            {
                DisplayCurrencyCode = "brl",
                FigureDate = new DateOnly(2026, 9, 5)
            });

        Assert.Equal("BRL", result.Data?.Totals.DisplayCurrencyCode);
        Assert.Equal(53.37m, result.Data?.Totals.DisplayExpense);
        Assert.Equal(15.02m, result.Data?.Totals.DisplayEarning);
        Assert.Equal(-38.35m, result.Data?.Totals.DisplayNet);
        Assert.Equal(
            result.Data?.Totals.DisplayEarning - result.Data?.Totals.DisplayExpense,
            result.Data?.Totals.DisplayNet);
        Assert.Equal(rate.RateDate, result.Data?.Totals.ByCurrency.Last().RateDate);
        Assert.Equal(new DateOnly(2026, 9, 5), rates.LastFigureDate);
    }

    [UnitFact]
    public async Task GivenMissingDisplayRate_WhenSearched_ThenRawTotalsAndReasonAreReturned()
    {
        var profile = Profile();
        var reader = new StubTransactionReader(
            [],
            [new TransactionCurrencyTotalSnapshot("USD", 10m, 0m)]);

        var result = await SearchHandler(profile, reader).HandleAsync(new SearchTransactionsQuery
        {
            DisplayCurrencyCode = "BRL"
        });

        Assert.Null(result.Data?.Totals.DisplayNet);
        Assert.Equal(FigureConversionMessages.RateUnavailable,
            result.Data?.Totals.ByCurrency.Single().UnconvertedReason);
        Assert.Contains(FigureConversionMessages.PartiallyConverted, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoMatchesAndDisplayCurrency_WhenSearched_ThenZeroDisplayTotalsAreReturned()
    {
        var profile = Profile();

        var result = await SearchHandler(profile, new StubTransactionReader()).HandleAsync(
            new SearchTransactionsQuery { DisplayCurrencyCode = "BRL" });

        Assert.Equal(0m, result.Data?.Totals.DisplayExpense);
        Assert.Equal(0m, result.Data?.Totals.DisplayEarning);
        Assert.Equal(0m, result.Data?.Totals.DisplayNet);
    }

    [UnitFact]
    public async Task GivenUnsupportedDisplayCurrency_WhenSearched_ThenCurrencyIsRejected()
    {
        var profile = Profile();

        var result = await SearchHandler(profile, new StubTransactionReader()).HandleAsync(
            new SearchTransactionsQuery { DisplayCurrencyCode = "ZZZ" });

        Assert.Contains(TransactionMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(TransactionMessages.UnknownCurrency("ZZZ"), result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenTransactionsAreSearched_ThenProfileNotFoundIsReturned()
    {
        var result = await SearchHandler(null, new StubTransactionReader()).HandleAsync(
            new SearchTransactionsQuery());

        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidPage_WhenTransactionsAreSearched_ThenValidationErrorIsReturned()
    {
        var result = await SearchHandler(Profile(), new StubTransactionReader()).HandleAsync(
            new SearchTransactionsQuery { PageNumber = 0 });

        Assert.Contains(TransactionMessages.InvalidPageNumber, result.Errors);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenTransactionsAreSearched_ThenPublicProfileLookupIsUsed()
    {
        var profile = Profile(null);
        var profiles = new StubProfileReader(profile);
        var handler = SearchHandler(
            profile,
            new StubTransactionReader(),
            profiles: profiles,
            actor: new RequestActor(profile.Id, 3, null, []) { IsLocal = true });

        await handler.HandleAsync(new SearchTransactionsQuery());

        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static GetTransactionByIdQueryHandler DetailHandler(
        UserProfileSnapshot? profile,
        ITransactionReader transactions) => new(
        new GetTransactionByIdQueryValidator(),
        new StubProfileReader(profile),
        transactions,
        Actor(profile));

    private static SearchTransactionsQueryHandler SearchHandler(
        UserProfileSnapshot? profile,
        ITransactionReader transactions,
        int maximumPageSize = 100,
        IExchangeRateReader? rates = null,
        StubProfileReader? profiles = null,
        RequestActor? actor = null) => new(
        new SearchTransactionsQueryValidator(),
        profiles ?? new StubProfileReader(profile),
        transactions,
        new StubCurrencyReader(),
        rates ?? new StubRateReader(),
        new StubActor(actor ?? ActorValue(profile)),
        new PaginationOptions(maximumPageSize),
        new FixedTimeProvider(Now));

    private static StubActor Actor(UserProfileSnapshot? profile) => new(ActorValue(profile));

    private static RequestActor ActorValue(UserProfileSnapshot? profile) => new(
        profile?.ExternalSubject ?? Guid.NewGuid(),
        3,
        null,
        []);

    private static UserProfileSnapshot Profile() => Profile(ExternalSubject);

    private static UserProfileSnapshot Profile(Guid? externalSubject) => new(
        Guid.NewGuid(),
        externalSubject,
        "Owner",
        "BRL",
        false,
        Now,
        Now);

    private static TransactionReadSnapshot Snapshot(
        Guid userId,
        string description,
        string currencyCode = "BRL",
        decimal amount = 10m,
        TransactionDirection direction = TransactionDirection.Expense,
        string categoryName = "Food",
        string counterpartyName = "Market",
        DateOnly? occurredOn = null,
        DateTimeOffset? createdAt = null,
        bool isDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = Guid.NewGuid(),
            FinancialAccountName = "Checking",
            CategoryId = Guid.NewGuid(),
            CategoryName = categoryName,
            CounterpartyId = Guid.NewGuid(),
            CounterpartyName = counterpartyName,
            Direction = direction,
            Amount = amount,
            CurrencyCode = currencyCode,
            OccurredOn = occurredOn ?? new DateOnly(2026, 9, 5),
            Description = description,
            SourceType = TransactionSourceType.Manual,
            Tags = [new(Guid.NewGuid(), "Daily")],
            IsDeleted = isDeleted,
            CreatedAt = createdAt ?? Now,
            UpdatedAt = (createdAt ?? Now).AddMinutes(1)
        };

    private sealed class StubTransactionReader : ITransactionReader
    {
        private readonly IReadOnlyCollection<TransactionReadSnapshot> snapshots;
        private readonly IReadOnlyCollection<TransactionCurrencyTotalSnapshot> totals;

        public StubTransactionReader(params TransactionReadSnapshot[] snapshots)
            : this(snapshots, [])
        {
        }

        public StubTransactionReader(
            IReadOnlyCollection<TransactionReadSnapshot> snapshots,
            IReadOnlyCollection<TransactionCurrencyTotalSnapshot> totals)
        {
            this.snapshots = snapshots;
            this.totals = totals;
        }

        public TransactionSearchCriteria? LastCriteria { get; private set; }

        public IQueryable<TransactionReadSnapshot> Query(TransactionSearchCriteria criteria)
        {
            LastCriteria = criteria;
            return snapshots.AsQueryable();
        }

        public Task<TransactionReadSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken) => Task.FromResult(snapshots.SingleOrDefault(item =>
            item.UserId == userId &&
            item.Id == id &&
            (includeDeleted || !item.IsDeleted)));

        public Task<IReadOnlyCollection<TransactionCurrencyTotalSnapshot>> SummarizeAsync(
            TransactionSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastCriteria = criteria;
            return Task.FromResult(totals);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
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

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        private static readonly IReadOnlyDictionary<string, CurrencySnapshot> Currencies =
            new Dictionary<string, CurrencySnapshot>
            {
                ["BRL"] = new("BRL", "Brazilian Real", 2),
                ["USD"] = new("USD", "US Dollar", 2)
            };

        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>(Currencies.Values.ToArray());

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult(Currencies.GetValueOrDefault(code));
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate = null) : IExchangeRateReader
    {
        public DateOnly? LastFigureDate { get; private set; }

        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken)
        {
            LastFigureDate = figureDate;
            return Task.FromResult(rate);
        }
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
