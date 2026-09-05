using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class InvestmentQueryHandlerTests
{
    private static readonly Guid ExternalSubject =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateOnly FigureDate = new(2026, 9, 5);

    [UnitFact]
    public async Task GivenOwnedInvestment_WhenRead_ThenPositionAndConversionAreReturned()
    {
        var profile = Profile();
        var investment = Position(profile.Id, "Fund", "USD", 12.34m);
        var rate = new ExchangeRateSnapshot(
            "USD", "BRL", 5m, FigureDate.AddDays(-1), ExchangeRateSource.Manual);
        var handler = DetailHandler(profile, new StubInvestmentReader(investment), rate);

        var result = await handler.HandleAsync(new GetInvestmentByIdQuery
        {
            Id = investment.Id,
            DisplayCurrencyCode = "brl",
            FigureDate = FigureDate
        });

        Assert.True(result.Success);
        Assert.Equal(12.34m, result.Data?.Position);
        Assert.Equal(61.70m, result.Data?.DisplayPosition);
        Assert.Equal(5m, result.Data?.AppliedRate);
        Assert.Equal(rate.RateDate, result.Data?.RateDate);
        Assert.Equal(rate.Source, result.Data?.RateSource);
    }

    [UnitFact]
    public async Task GivenMissingForeignOrUnknownProfile_WhenRead_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var foreign = Position(Guid.NewGuid(), "Private", "BRL", 1m);

        var foreignResult = await DetailHandler(
            profile,
            new StubInvestmentReader(foreign)).HandleAsync(new GetInvestmentByIdQuery
            {
                Id = foreign.Id
            });
        var missingProfile = await DetailHandler(
            null,
            new StubInvestmentReader()).HandleAsync(new GetInvestmentByIdQuery
            {
                Id = Guid.NewGuid()
            });

        Assert.Contains(InvestmentMessages.NotFound, foreignResult.Errors);
        Assert.Contains(InvestmentMessages.ProfileNotFound, missingProfile.Errors);
    }

    [UnitFact]
    public async Task GivenUnavailableOrSameCurrencyRate_WhenRead_ThenConversionIsPartialOrDirect()
    {
        var profile = Profile();
        var reader = new StubInvestmentReader(Position(profile.Id, "Fund", "USD", 1.005m));
        var unavailable = await DetailHandler(profile, reader).HandleAsync(new GetInvestmentByIdQuery
        {
            Id = reader.Positions[0].Id,
            DisplayCurrencyCode = "BRL"
        });
        var direct = await DetailHandler(profile, reader).HandleAsync(new GetInvestmentByIdQuery
        {
            Id = reader.Positions[0].Id,
            DisplayCurrencyCode = "USD"
        });

        Assert.True(unavailable.Success);
        Assert.Null(unavailable.Data?.DisplayPosition);
        Assert.Equal(FigureConversionMessages.RateUnavailable, unavailable.Data?.UnconvertedReason);
        Assert.Equal(1.01m, direct.Data?.DisplayPosition);
        Assert.Null(direct.Data?.AppliedRate);
    }

    [UnitFact]
    public async Task GivenUnsupportedDisplayCurrency_WhenRead_ThenItIsRejected()
    {
        var profile = Profile();
        var investment = Position(profile.Id, "Fund", "BRL", 1m);

        var result = await DetailHandler(
            profile,
            new StubInvestmentReader(investment)).HandleAsync(new GetInvestmentByIdQuery
            {
                Id = investment.Id,
                DisplayCurrencyCode = "ZZZ"
            });

        Assert.Contains(InvestmentMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(InvestmentMessages.UnknownCurrency("ZZZ"), result.Messages);
    }

    [UnitTheory]
    [InlineData("Instrument", false, "Alpha")]
    [InlineData("Instrument", true, "Beta")]
    [InlineData("Institution", false, "Beta")]
    [InlineData("Institution", true, "Alpha")]
    [InlineData("InvestmentType", false, "Alpha")]
    [InlineData("InvestmentType", true, "Beta")]
    [InlineData("CurrencyCode", false, "Beta")]
    [InlineData("CurrencyCode", true, "Alpha")]
    [InlineData("Position", false, "Alpha")]
    [InlineData("Position", true, "Beta")]
    [InlineData("CreatedAt", false, "Beta")]
    [InlineData("CreatedAt", true, "Alpha")]
    [InlineData("UpdatedAt", false, "Beta")]
    [InlineData("UpdatedAt", true, "Alpha")]
    public async Task GivenSupportedSort_WhenListed_ThenRequestedOrderingIsApplied(
        string sortBy,
        bool descending,
        string expectedFirst)
    {
        var profile = Profile();
        var alpha = Position(
            profile.Id,
            "Alpha",
            "USD",
            10m,
            "Z Bank",
            InvestmentType.FixedIncome,
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var beta = Position(
            profile.Id,
            "Beta",
            "BRL",
            20m,
            "A Bank",
            InvestmentType.Fund,
            new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero));

        var result = await ListHandler(
            profile,
            new StubInvestmentReader(alpha, beta)).HandleAsync(new ListInvestmentsQuery
            {
                SortBy = sortBy,
                Descending = descending
            });

        Assert.Equal(expectedFirst, result.Data?.First().Instrument);
    }

    [UnitFact]
    public async Task GivenFiltersAndPageLimit_WhenListed_ThenOnlyOwnedLiveMatchesAreReturned()
    {
        var profile = Profile();
        var matching = Position(profile.Id, "Reserve", "USD", 10m, "Broker", InvestmentType.Fund);
        var foreign = Position(Guid.NewGuid(), "Reserve Foreign", "USD", 20m, "Broker", InvestmentType.Fund);
        var deleted = Position(profile.Id, "Reserve Deleted", "USD", 30m, "Broker", InvestmentType.Fund);
        deleted = Copy(deleted, isDeleted: true);

        var result = await ListHandler(
            profile,
            new StubInvestmentReader(matching, foreign, deleted),
            maximumPageSize: 2).HandleAsync(new ListInvestmentsQuery
            {
                Instrument = "serve",
                Institution = "ROK",
                InvestmentType = InvestmentType.Fund,
                CurrencyCode = "usd",
                PageSize = 500
            });

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal(2, result.PageSize);
        Assert.Contains(InvestmentMessages.ListedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData("ValuedOn", false, 10)]
    [InlineData("ValuedOn", true, 20)]
    [InlineData("Value", false, 10)]
    [InlineData("Value", true, 20)]
    [InlineData("CreatedAt", false, 20)]
    [InlineData("CreatedAt", true, 10)]
    [InlineData("UpdatedAt", false, 20)]
    [InlineData("UpdatedAt", true, 10)]
    public async Task GivenSupportedValuationSort_WhenListed_ThenOrderingIsApplied(
        string sortBy,
        bool descending,
        int expectedFirst)
    {
        var profile = Profile();
        var investment = Position(profile.Id, "Fund", "BRL", 20m);
        var first = Valuation(
            investment.Id,
            10m,
            FigureDate.AddDays(-1),
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var second = Valuation(
            investment.Id,
            20m,
            FigureDate,
            new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero));
        var handler = HistoryHandler(
            profile,
            new StubInvestmentReader([investment], [first, second]));

        var result = await handler.HandleAsync(new ListInvestmentValuationsQuery
        {
            InvestmentId = investment.Id,
            SortBy = sortBy,
            Descending = descending
        });

        Assert.Equal(expectedFirst, result.Data?.First().Value);
    }

    [UnitFact]
    public async Task GivenPeriodWithoutValues_WhenHistoryViewed_ThenEmptyPageSucceeds()
    {
        var profile = Profile();
        var investment = Position(profile.Id, "Fund", "BRL", 0m);
        var handler = HistoryHandler(profile, new StubInvestmentReader(investment));

        var result = await handler.HandleAsync(new ListInvestmentValuationsQuery
        {
            InvestmentId = investment.Id,
            From = FigureDate,
            To = FigureDate
        });

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Contains(InvestmentMessages.ValuationHistoryRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidQueries_WhenValidated_ThenAllContractErrorsAreReturned()
    {
        var get = await new GetInvestmentByIdQueryValidator().ValidateAsync(
            new GetInvestmentByIdQuery { DisplayCurrencyCode = "12" });
        var list = await new ListInvestmentsQueryValidator().ValidateAsync(new ListInvestmentsQuery
        {
            PageNumber = 0,
            PageSize = 0,
            Instrument = new string('i', 201),
            Institution = new string('b', 201),
            InvestmentType = (InvestmentType)999,
            CurrencyCode = "US",
            DisplayCurrencyCode = "123",
            SortBy = "Balance"
        });
        var history = await new ListInvestmentValuationsQueryValidator().ValidateAsync(
            new ListInvestmentValuationsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                From = FigureDate,
                To = FigureDate.AddDays(-1),
                SortBy = "Currency"
            });

        Assert.Contains(get.Errors, failure => failure.ErrorMessage == InvestmentMessages.InvestmentIdRequired);
        Assert.Contains(get.Errors, failure => failure.ErrorMessage == InvestmentMessages.DisplayCurrencyInvalid);
        Assert.Equal(8, list.Errors.Count);
        Assert.Equal(5, history.Errors.Count);
    }

    private static GetInvestmentByIdQueryHandler DetailHandler(
        UserProfileSnapshot? profile,
        IInvestmentReader investments,
        ExchangeRateSnapshot? rate = null) => new(
        new GetInvestmentByIdQueryValidator(),
        new StubProfileReader(profile),
        investments,
        new StubCurrencyReader(),
        new StubRateReader(rate),
        Actor(profile),
        TimeProvider.System);

    private static ListInvestmentsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        IInvestmentReader investments,
        int maximumPageSize = 100) => new(
        new ListInvestmentsQueryValidator(),
        new StubProfileReader(profile),
        investments,
        new StubCurrencyReader(),
        new StubRateReader(null),
        Actor(profile),
        new PaginationOptions(maximumPageSize),
        TimeProvider.System);

    private static ListInvestmentValuationsQueryHandler HistoryHandler(
        UserProfileSnapshot? profile,
        IInvestmentReader investments) => new(
        new ListInvestmentValuationsQueryValidator(),
        new StubProfileReader(profile),
        investments,
        Actor(profile),
        new PaginationOptions(100));

    private static StubActor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        ExternalSubject,
        "Investor",
        "BRL",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static InvestmentPositionSnapshot Position(
        Guid userId,
        string instrument,
        string currencyCode,
        decimal position,
        string? institution = "Broker",
        InvestmentType investmentType = InvestmentType.Fund,
        DateTimeOffset? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Instrument = instrument,
            Institution = institution,
            InvestmentType = investmentType,
            CurrencyCode = currencyCode,
            Position = position,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = createdAt?.AddHours(1) ?? DateTimeOffset.UtcNow
        };

    private static InvestmentPositionSnapshot Copy(
        InvestmentPositionSnapshot source,
        bool isDeleted) => new()
        {
            Id = source.Id,
            UserId = source.UserId,
            Instrument = source.Instrument,
            Institution = source.Institution,
            InvestmentType = source.InvestmentType,
            CurrencyCode = source.CurrencyCode,
            Position = source.Position,
            IsDeleted = isDeleted,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };

    private static InvestmentValuationReadSnapshot Valuation(
        Guid investmentId,
        decimal value,
        DateOnly valuedOn,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            InvestmentId = investmentId,
            Value = value,
            CurrencyCode = "BRL",
            ValuedOn = valuedOn,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddHours(1)
        };

    private sealed class StubInvestmentReader : IInvestmentReader
    {
        private readonly IReadOnlyList<InvestmentValuationReadSnapshot> valuations;

        public StubInvestmentReader(params InvestmentPositionSnapshot[] positions)
            : this(positions, [])
        {
        }

        public StubInvestmentReader(
            IReadOnlyList<InvestmentPositionSnapshot> positions,
            IReadOnlyList<InvestmentValuationReadSnapshot> valuations)
        {
            Positions = positions;
            this.valuations = valuations;
        }

        public IReadOnlyList<InvestmentPositionSnapshot> Positions { get; }

        public IQueryable<InvestmentPositionSnapshot> QueryPositions() => Positions.AsQueryable();

        public Task<InvestmentPositionSnapshot?> FindByIdWithPositionAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) => Task.FromResult(Positions.SingleOrDefault(item =>
                item.UserId == userId && item.Id == id && !item.IsDeleted));

        public IQueryable<InvestmentValuationReadSnapshot> QueryValuations(
            Guid userId,
            Guid investmentId) => valuations
            .Where(item => item.InvestmentId == investmentId)
            .AsQueryable();
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

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken) => Task.FromResult(rate);
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
