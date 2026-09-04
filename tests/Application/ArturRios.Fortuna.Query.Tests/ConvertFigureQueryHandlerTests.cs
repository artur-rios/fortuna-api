using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ConvertFigureQueryHandlerTests
{
    private static readonly DateOnly FigureDate = new(2026, 9, 4);

    [UnitFact]
    public async Task GivenSeveralCurrencies_WhenConverted_ThenGroupsAreTotalledBeforeOneRounding()
    {
        var rateDate = FigureDate.AddDays(-1);
        var rates = new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 1.5m, rateDate, ExchangeRateSource.Manual));
        var handler = Handler(rates: rates);

        var result = await handler.HandleAsync(new ConvertFigureQuery
        {
            ExternalSubject = Profile().ExternalSubject!.Value,
            FigureDate = FigureDate,
            Amounts =
            [
                new FigureAmountInput { Amount = 0.335m, CurrencyCode = "usd" },
                new FigureAmountInput { Amount = 0.335m, CurrencyCode = "USD" },
                new FigureAmountInput { Amount = 2m, CurrencyCode = "BRL" }
            ]
        });

        Assert.True(result.Success);
        Assert.True(result.Data!.IsFullyConverted);
        Assert.Equal("BRL", result.Data.DisplayCurrencyCode);
        Assert.Equal(3.01m, result.Data.Total);
        Assert.Collection(
            result.Data.Groups,
            group =>
            {
                Assert.Equal("BRL", group.SourceCurrencyCode);
                Assert.Equal(2m, group.DisplayAmount);
                Assert.Null(group.AppliedRate);
                Assert.Null(group.RateDate);
            },
            group =>
            {
                Assert.Equal("USD", group.SourceCurrencyCode);
                Assert.Equal(0.67m, group.SourceAmount);
                Assert.Equal(1.01m, group.DisplayAmount);
                Assert.Equal(1.5m, group.AppliedRate);
                Assert.Equal(rateDate, group.RateDate);
                Assert.Equal(ExchangeRateSource.Manual, group.RateSource);
            });
        Assert.Equal(1, rates.CallCount);
    }

    [UnitFact]
    public async Task GivenNegativeMidpoint_WhenConverted_ThenItRoundsAwayFromZero()
    {
        var handler = Handler(rates: new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 1m, FigureDate, ExchangeRateSource.Published)));

        var result = await handler.HandleAsync(Query([
            new FigureAmountInput { Amount = -1.005m, CurrencyCode = "USD" }
        ]));

        Assert.Equal(-1.01m, result.Data!.Total);
    }

    [UnitFact]
    public async Task GivenAnExplicitDisplayCurrency_WhenConverted_ThenNoProfileLookupIsNeeded()
    {
        var profiles = new StubProfileReader(null);
        var handler = Handler(profiles: profiles, rates: new StubRateReader(new ExchangeRateSnapshot(
            "BRL", "USD", 0.2m, FigureDate, ExchangeRateSource.Published)));
        var query = Query([new FigureAmountInput { Amount = 10m, CurrencyCode = "BRL" }]);
        query.DisplayCurrencyCode = "usd";

        var result = await handler.HandleAsync(query);

        Assert.True(result.Success);
        Assert.Equal("USD", result.Data!.DisplayCurrencyCode);
        Assert.Equal(2m, result.Data.Total);
        Assert.Equal(0, profiles.CallCount);
    }

    [UnitFact]
    public async Task GivenNoRateEverStored_WhenConverted_ThenTheFigureRemainsSplitAndUntotalled()
    {
        var handler = Handler(rates: new StubRateReader(null));

        var result = await handler.HandleAsync(Query([
            new FigureAmountInput { Amount = 10m, CurrencyCode = "USD" },
            new FigureAmountInput { Amount = 5m, CurrencyCode = "BRL" }
        ]));

        Assert.True(result.Success);
        Assert.False(result.Data!.IsFullyConverted);
        Assert.Null(result.Data.Total);
        var unresolved = Assert.Single(result.Data.Groups, group => group.SourceCurrencyCode == "USD");
        Assert.Null(unresolved.DisplayAmount);
        Assert.Equal(FigureConversionMessages.RateUnavailable, unresolved.UnconvertedReason);
        Assert.Contains(FigureConversionMessages.PartiallyConverted, result.Messages);
    }

    [UnitFact]
    public async Task GivenOnlyDisplayCurrencyAmounts_WhenConverted_ThenNoRateIsRequested()
    {
        var rates = new StubRateReader(null);
        var handler = Handler(rates: rates);

        var result = await handler.HandleAsync(Query([
            new FigureAmountInput { Amount = 1.005m, CurrencyCode = "BRL" },
            new FigureAmountInput { Amount = 2m, CurrencyCode = "brl" }
        ]));

        Assert.True(result.Data!.IsFullyConverted);
        Assert.Equal(3.01m, result.Data.Total);
        Assert.Null(Assert.Single(result.Data.Groups).AppliedRate);
        Assert.Equal(0, rates.CallCount);
    }

    [UnitFact]
    public async Task GivenUnsupportedDisplayCurrency_WhenConverted_ThenTheRequestIsRejected()
    {
        var query = Query([]);
        query.DisplayCurrencyCode = "ZZZ";

        var result = await Handler().HandleAsync(query);

        Assert.False(result.Success);
        Assert.Contains(FigureConversionMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(FigureConversionMessages.UnknownCurrency("ZZZ"), result.Messages);
    }

    [UnitFact]
    public async Task GivenUnsupportedSourceCurrency_WhenConverted_ThenTheRequestIsRejected()
    {
        var result = await Handler().HandleAsync(Query([
            new FigureAmountInput { Amount = 1m, CurrencyCode = "ZZZ" }
        ]));

        Assert.False(result.Success);
        Assert.Contains(FigureConversionMessages.CurrencyNotSupported, result.Errors);
        Assert.Contains(FigureConversionMessages.UnknownCurrency("ZZZ"), result.Messages);
    }

    [UnitFact]
    public async Task GivenNoDefaultProfile_WhenConverted_ThenNotFoundIsReturned()
    {
        var result = await Handler(profiles: new StubProfileReader(null)).HandleAsync(Query([]));

        Assert.False(result.Success);
        Assert.Contains(FigureConversionMessages.ProfileNotFound, result.Errors);
    }

    private static ConvertFigureQueryHandler Handler(
        StubRateReader? rates = null,
        StubProfileReader? profiles = null) => new(
            new ConvertFigureQueryValidator(),
            new StubCurrencyReader(),
            rates ?? new StubRateReader(null),
            profiles ?? new StubProfileReader(Profile()));

    private static ConvertFigureQuery Query(IReadOnlyCollection<FigureAmountInput> amounts) => new()
    {
        ExternalSubject = Profile().ExternalSubject!.Value,
        FigureDate = FigureDate,
        Amounts = amounts
    };

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        "Ada",
        "BRL",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        private static readonly IReadOnlyDictionary<string, CurrencySnapshot> Currencies =
            new Dictionary<string, CurrencySnapshot>(StringComparer.Ordinal)
            {
                ["BRL"] = new("BRL", "Brazilian Real", 2),
                ["USD"] = new("USD", "US Dollar", 2),
                ["JPY"] = new("JPY", "Yen", 0)
            };

        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>(Currencies.Values.ToArray());

        public Task<CurrencySnapshot?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Currencies.GetValueOrDefault(code));
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public int CallCount { get; private set; }

        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(rate);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public int CallCount { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(profile);
        }

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(profile);
        }
    }
}
