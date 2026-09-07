using System.Text;
using System.Text.Json;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class AggregateTransactionsQueryHandlerTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);

    [UnitFact]
    public async Task GivenPeriodFigures_WhenHandled_ThenGapsConversionsSharesAndKeysReturn()
    {
        var reader = new StubAggregationReader([
            new("2026-09-01", "2026-09-01", Start, "BRL", Start, -10m),
            new("2026-09-03", "2026-09-03", Start.AddDays(2), "USD", Start.AddDays(2), -2m)
        ]);
        var handler = Handler(reader: reader, rate: new ExchangeRateSnapshot(
            "USD", "BRL", 5m, Start.AddDays(2), ExchangeRateSource.Manual));
        var query = Valid();
        query.FinancialAccountId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        query.Text = " coffee ";

        var result = await handler.HandleAsync(query);

        Assert.True(result.Success);
        Assert.Equal(Profile().Id, reader.Criteria!.UserId);
        Assert.Equal("coffee", reader.Criteria.Text);
        Assert.Equal("BRL", result.Data!.DisplayCurrencyCode);
        Assert.True(result.Data.IsFullyConverted);
        Assert.Equal(3, result.Data.Buckets.Count);
        var buckets = result.Data.Buckets.ToArray();
        Assert.Equal(-10m, buckets[0].Total);
        Assert.Equal(0m, buckets[1].Total);
        Assert.Empty(buckets[1].Conversions);
        Assert.Equal(-10m, buckets[2].Total);
        Assert.Equal(0.5m, buckets[0].Share);
        Assert.Equal(0m, buckets[1].Share);
        Assert.Equal(5m, Assert.Single(buckets[2].Conversions).AppliedRate);
        using var key = Decode(buckets[0].DrillDownKey);
        Assert.Equal("period", key.RootElement.GetProperty("dimension").GetString());
        Assert.Equal("2026-09-01", key.RootElement.GetProperty("bucket").GetString());
        Assert.Contains(TransactionAggregationMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenUnavailableRate_WhenHandled_ThenBucketAndSharesArePartial()
    {
        var handler = Handler(reader: new StubAggregationReader([
            new("category", "Category", null, "USD", Start, -2m)
        ]));
        var query = Valid();
        query.Dimension = "category";
        query.Granularity = null;

        var result = await handler.HandleAsync(query);

        var bucket = Assert.Single(result.Data!.Buckets);
        Assert.False(result.Data.IsFullyConverted);
        Assert.False(bucket.IsFullyConverted);
        Assert.Null(bucket.Total);
        Assert.Null(bucket.Share);
        Assert.Equal(FigureConversionMessages.RateUnavailable,
            Assert.Single(bucket.Conversions).UnconvertedReason);
    }

    [UnitFact]
    public async Task GivenInvalidInputMissingProfileOrCurrency_WhenHandled_ThenReaderIsNotCalled()
    {
        var invalidReader = new StubAggregationReader([]);
        var invalid = Valid();
        invalid.To = null;
        var invalidResult = await Handler(reader: invalidReader).HandleAsync(invalid);
        var missingReader = new StubAggregationReader([]);
        var missingResult = await Handler(reader: missingReader, missingProfile: true)
            .HandleAsync(Valid());
        var currencyReader = new StubAggregationReader([]);
        var currencyQuery = Valid();
        currencyQuery.DisplayCurrencyCode = "ZZZ";
        var currencyResult = await Handler(reader: currencyReader)
            .HandleAsync(currencyQuery);

        Assert.Contains(TransactionAggregationMessages.ToRequired, invalidResult.Errors);
        Assert.Contains(TransactionAggregationMessages.ProfileNotFound, missingResult.Errors);
        Assert.Contains(TransactionAggregationMessages.DisplayCurrencyUnsupported,
            currencyResult.Errors);
        Assert.All([invalidReader, missingReader, currencyReader], item =>
            Assert.Null(item.Criteria));
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenHandled_ThenProfileIsResolvedByPublicId()
    {
        var profiles = new StubProfileReader(Profile());
        var actor = new RequestActor(Profile().Id, 3, null, []) { IsLocal = true };

        var result = await Handler(profiles: profiles, actor: actor).HandleAsync(Valid());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static AggregateTransactionsQueryHandler Handler(
        StubAggregationReader? reader = null,
        bool missingProfile = false,
        StubProfileReader? profiles = null,
        RequestActor? actor = null,
        ExchangeRateSnapshot? rate = null)
    {
        var resolved = missingProfile ? null : Profile();
        return new AggregateTransactionsQueryHandler(
            new AggregateTransactionsQueryValidator(new TransactionAggregationOptions(366)),
            profiles ?? new StubProfileReader(resolved),
            reader ?? new StubAggregationReader([]),
            new StubCurrencyReader(),
            new StubRateReader(rate),
            new StubActor(actor ?? new RequestActor(
                resolved?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])));
    }

    private static AggregateTransactionsQuery Valid() => new()
    {
        Dimension = "period",
        Granularity = "day",
        From = Start,
        To = Start.AddDays(2)
    };

    private static UserProfileSnapshot Profile() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        "Owner",
        "BRL",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static JsonDocument Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)));
    }

    private sealed class StubAggregationReader(
        IReadOnlyCollection<TransactionAggregationFigureSnapshot> figures)
        : ITransactionAggregationReader
    {
        public TransactionAggregationCriteria? Criteria { get; private set; }

        public Task<IReadOnlyCollection<TransactionAggregationFigureSnapshot>> ReadAsync(
            TransactionAggregationCriteria criteria,
            CancellationToken cancellationToken)
        {
            Criteria = criteria;
            return Task.FromResult(figures);
        }
    }

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) => Task.FromResult<CurrencySnapshot?>(
            code is "BRL" or "USD" ? new CurrencySnapshot(code, code, 2) : null);
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken) => Task.FromResult(rate);
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

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
