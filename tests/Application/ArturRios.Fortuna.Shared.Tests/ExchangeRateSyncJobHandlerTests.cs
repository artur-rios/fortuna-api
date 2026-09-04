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
    public async Task GivenUnreachableSource_WhenJobRuns_ThenSafeFailureReasonIsRaised()
    {
        var handler = Handler(new StubClient(new HttpRequestException("host leaked")), new StubRateStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ExecuteAsync("{\"RequestedDate\":\"2026-09-01\"}", CancellationToken.None));

        Assert.Equal(ExchangeRateSyncMessages.SourceUnavailable, exception.Message);
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
        private readonly PtaxQuoteBatch? batch;
        private readonly Exception? exception;

        public StubClient(PtaxQuoteBatch batch) => this.batch = batch;
        public StubClient(Exception exception) => this.exception = exception;

        public Task<PtaxQuoteBatch> GetLatestQuotesAsync(
            IReadOnlyCollection<string> currencyCodes,
            DateOnly requestedDate,
            CancellationToken cancellationToken) => exception is null
                ? Task.FromResult(batch!)
                : Task.FromException<PtaxQuoteBatch>(exception);
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
