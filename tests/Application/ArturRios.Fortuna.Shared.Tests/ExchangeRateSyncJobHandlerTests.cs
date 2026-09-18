using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class ExchangeRateSyncJobHandlerTests
{
    [UnitFact]
    public async Task GivenPublishedParities_WhenJobRuns_ThenEveryDirectedCrossRateIsStored()
    {
        var publicationDate = new DateOnly(2026, 9, 1);
        var store = new StubRateStore();
        var handler = Handler(
            new StubClient(new PtaxQuoteBatch(publicationDate, [
                new PtaxQuote("USD", 5m),
                new PtaxQuote("EUR", 6m)
            ])),
            store);

        await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-02\"}", CancellationToken.None);

        Assert.Equal(6, store.Rates!.Count);
        Assert.Contains(store.Rates, rate =>
            rate is { BaseCurrencyCode: "USD", QuoteCurrencyCode: "EUR", PublicationDate: var date } &&
            rate.Rate == 5m / 6m && date == publicationDate);
        Assert.Contains(store.Rates, rate =>
            rate is { BaseCurrencyCode: "EUR", QuoteCurrencyCode: "BRL", Rate: 6m });
        Assert.Contains(store.Rates, rate =>
            rate is { BaseCurrencyCode: "BRL", QuoteCurrencyCode: "USD", Rate: 0.2m });
    }

    [UnitFact]
    public async Task GivenInvalidSourceRow_WhenJobRuns_ThenItIsRejectedAndOtherRatesContinue()
    {
        var store = new StubRateStore();
        var handler = Handler(
            new StubClient(new PtaxQuoteBatch(new DateOnly(2026, 9, 1), [
                new PtaxQuote("USD", 5m),
                new PtaxQuote("EUR", 0m)
            ])),
            store);

        await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.Equal(2, store.Rates!.Count);
        Assert.DoesNotContain(store.Rates, rate =>
            rate.BaseCurrencyCode == "EUR" || rate.QuoteCurrencyCode == "EUR");
    }

    [UnitFact]
    public async Task GivenDuplicateAndLowercaseSourceRows_WhenJobRuns_ThenFirstRowWinsAndBrlIsNotDuplicated()
    {
        var store = new StubRateStore();
        var handler = Handler(
            new StubClient(new PtaxQuoteBatch(new DateOnly(2026, 9, 1), [
                new PtaxQuote("USD", 5m),
                new PtaxQuote("usd", 7m),
                new PtaxQuote("brl", 3m)
            ])),
            store);

        var result = await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, store.Rates!.Count);
        Assert.Contains(store.Rates, rate => rate is { BaseCurrencyCode: "USD", QuoteCurrencyCode: "BRL", Rate: 5m });
    }

    [UnitFact]
    public async Task GivenLowercaseBaseCurrencyInOptions_WhenJobRuns_ThenItIsNotRequestedFromTheSource()
    {
        var client = new StubClient(new PtaxQuoteBatch(new DateOnly(2026, 9, 1), [new PtaxQuote("USD", 5m)]));
        var handler = new ExchangeRateSyncJobHandler(
            client,
            new StubRateStore(),
            new RateSyncOptions(new Uri("https://rates.example.test/"), "0 18 * * 1-5", ["brl", "usd"]),
            NullLogger<ExchangeRateSyncJobHandler>.Instance);

        await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.Equal(["USD"], client.RequestedCurrencies);
    }

    [UnitFact]
    public async Task GivenCurrencyMissingFromSource_WhenJobRuns_ThenOthersAreStoredAndTheGapIsReported()
    {
        var store = new StubRateStore();
        var handler = Handler(
            new StubClient(new PtaxQuoteBatch(new DateOnly(2026, 9, 1), [new PtaxQuote("USD", 5m)])),
            store);

        var result = await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, store.Rates!.Count);
        Assert.Contains(ExchangeRateSyncMessages.CurrenciesMissing(["EUR"]), result.Messages);
    }

    [UnitFact]
    public async Task GivenNoPublication_WhenJobRuns_ThenPublicationUnavailableIsReturned()
    {
        var handler = Handler(new StubClient(PtaxQuoteOutcome.PublicationUnavailable), new StubRateStore());

        var result = await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.Equal([ExchangeRateSyncMessages.PublicationUnavailable], result.Errors);
    }

    [UnitFact]
    public async Task GivenUnreachableSource_WhenJobRuns_ThenSafeFailureReasonIsRaised()
    {
        var handler = Handler(new StubClient(PtaxQuoteOutcome.SourceUnavailable), new StubRateStore());

        var result = await handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None);

        Assert.Equal([ExchangeRateSyncMessages.SourceUnavailable], result.Errors);
    }

    private static ExchangeRateSyncJobHandler Handler(IPtaxRateClient client, IExchangeRateStore store) =>
        new(
            client,
            store,
            new RateSyncOptions(
                new Uri("https://rates.example.test/"),
                "0 18 * * 1-5",
                ["BRL", "USD", "EUR"]),
            NullLogger<ExchangeRateSyncJobHandler>.Instance);

    private sealed class StubClient : IPtaxRateClient
    {
        private readonly PtaxQuoteResult result;

        public StubClient(PtaxQuoteBatch batch) => result = PtaxQuoteResult.Succeeded(batch);
        public StubClient(PtaxQuoteOutcome outcome) => result = PtaxQuoteResult.Failed(outcome);

        public IReadOnlyCollection<string>? RequestedCurrencies { get; private set; }

        public Task<PtaxQuoteResult> GetLatestQuotesAsync(
            IReadOnlyCollection<string> currencyCodes,
            DateOnly requestedDate,
            CancellationToken cancellationToken)
        {
            RequestedCurrencies = currencyCodes;

            return Task.FromResult(result);
        }
    }

    private sealed class StubRateStore : IExchangeRateStore
    {
        public IReadOnlyCollection<PublishedRateCandidate>? Rates { get; private set; }

        public Task<PublishedRateUpsertResult> UpsertPublishedAsync(
            IReadOnlyCollection<PublishedRateCandidate> rates,
            CancellationToken cancellationToken)
        {
            Rates = rates;

            return Task.FromResult(new PublishedRateUpsertResult(rates.Count, 0));
        }

        public Task<ManualRateUpsertResult> UpsertManualAsync(
            ManualRateCandidate rate,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
