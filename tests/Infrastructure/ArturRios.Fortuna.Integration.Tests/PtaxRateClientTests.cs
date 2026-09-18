using System.Net;
using System.Net.Http.Headers;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class PtaxRateClientTests
{
    [UnitFact]
    public async Task GivenWeekendWithoutPublication_WhenRatesAreRead_ThenLatestCommonClosingPublicationIsUsed()
    {
        var handler = new FixtureHandler();
        var client = Client(handler, new StubDelay());

        var result = Batch(await client.GetLatestQuotesAsync(
            ["EUR", "USD"],
            new DateOnly(2026, 9, 6),
            CancellationToken.None));

        Assert.Equal(new DateOnly(2026, 9, 4), result.PublicationDate);
        Assert.Empty(result.MissingCurrencies);
        Assert.Contains(result.Quotes, quote => quote is { CurrencyCode: "EUR", BrlPerUnit: 6.03m });
        Assert.Contains(result.Quotes, quote => quote is { CurrencyCode: "USD", BrlPerUnit: 5.12m });
        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("08-30-2026", request, StringComparison.Ordinal);
            Assert.Contains("09-06-2026", request, StringComparison.Ordinal);
            Assert.Contains("%24format=json", request, StringComparison.Ordinal);
        });
    }

    [UnitFact]
    public async Task GivenRateLimitResponse_WhenRatesAreRead_ThenRetryAfterIsObservedAndRequestResumes()
    {
        var handler = new RateLimitedHandler();
        var delay = new StubDelay();
        var client = Client(handler, delay);

        var result = Batch(await client.GetLatestQuotesAsync(
            ["USD"],
            new DateOnly(2026, 9, 4),
            CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal([TimeSpan.FromSeconds(3)], delay.Delays);
        Assert.Equal(5.12m, Assert.Single(result.Quotes).BrlPerUnit);
    }

    [UnitFact]
    public async Task GivenHugeRetryAfter_WhenRatesAreRead_ThenThePauseIsCapped()
    {
        var handler = new RateLimitedHandler(TimeSpan.FromHours(6));
        var delay = new StubDelay();

        await Client(handler, delay).GetLatestQuotesAsync(["USD"], new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Equal([HttpRetryPolicy.MaximumRetryAfter], delay.Delays);
    }

    [UnitFact]
    public async Task GivenTransientServerErrors_WhenRatesAreRead_ThenRequestIsRetriedWithinTheBound()
    {
        var handler = new StatusHandler(HttpStatusCode.ServiceUnavailable, failures: 2);
        var delay = new StubDelay();

        var result = await Client(handler, delay).GetLatestQuotesAsync(
            ["USD"], new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Equal(PtaxQuoteOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [UnitFact]
    public async Task GivenPersistentServerErrors_WhenRatesAreRead_ThenSourceUnavailableIsReturnedAfterTheBound()
    {
        var handler = new StatusHandler(HttpStatusCode.BadGateway, failures: int.MaxValue);

        var result = await Client(handler, new StubDelay()).GetLatestQuotesAsync(
            ["USD"], new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Equal(PtaxQuoteOutcome.SourceUnavailable, result.Outcome);
        Assert.Equal(HttpRetryPolicy.MaximumAttempts, handler.CallCount);
    }

    [UnitFact]
    public async Task GivenClientError_WhenRatesAreRead_ThenItIsNotRetried()
    {
        var handler = new StatusHandler(HttpStatusCode.BadRequest, failures: int.MaxValue);

        var result = await Client(handler, new StubDelay()).GetLatestQuotesAsync(
            ["USD"], new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Equal(PtaxQuoteOutcome.SourceUnavailable, result.Outcome);
        Assert.Equal(1, handler.CallCount);
    }

    [UnitFact]
    public async Task GivenUnreachableSource_WhenRatesAreRead_ThenSourceUnavailableIsReturned()
    {
        var result = await Client(new ThrowingHandler(), new StubDelay()).GetLatestQuotesAsync(
            ["USD"], new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Equal(PtaxQuoteOutcome.SourceUnavailable, result.Outcome);
    }

    [UnitFact]
    public async Task GivenCurrencyWithoutPublications_WhenRatesAreRead_ThenItIsReportedMissing()
    {
        var result = Batch(await Client(new EmptyForHandler("GBP"), new StubDelay()).GetLatestQuotesAsync(
            ["USD", "gbp"], new DateOnly(2026, 9, 6), CancellationToken.None));

        Assert.Equal(["GBP"], result.MissingCurrencies);
        Assert.Equal("USD", Assert.Single(result.Quotes).CurrencyCode);
    }

    [UnitFact]
    public async Task GivenNoCommonPriorPublication_WhenRatesAreRead_ThenPublicationUnavailableIsReturned()
    {
        var client = Client(new NoCommonDateHandler(), new StubDelay());

        var result = await client.GetLatestQuotesAsync(
            ["EUR", "USD"],
            new DateOnly(2026, 9, 4),
            CancellationToken.None);

        Assert.Equal(PtaxQuoteOutcome.PublicationUnavailable, result.Outcome);
        Assert.Null(result.Batch);
    }

    [UnitTheory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    public void GivenNoRetryAfterHeader_WhenDelayIsComputed_ThenBackoffIsExponential(int attempt, int seconds)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Assert.Equal(TimeSpan.FromSeconds(seconds), HttpRetryPolicy.RetryDelay(response, attempt, DateTimeOffset.UtcNow));
    }

    [UnitFact]
    public void GivenRetryAfterDate_WhenDelayIsComputed_ThenItIsRelativeToNowAndCapped()
    {
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        using var soon = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        soon.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(5));
        using var late = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        late.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddDays(1));
        using var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(-5));

        Assert.Equal(TimeSpan.FromSeconds(5), HttpRetryPolicy.RetryDelay(soon, 1, now));
        Assert.Equal(HttpRetryPolicy.MaximumRetryAfter, HttpRetryPolicy.RetryDelay(late, 1, now));
        Assert.Equal(TimeSpan.Zero, HttpRetryPolicy.RetryDelay(past, 1, now));
    }

    private static PtaxQuoteBatch Batch(PtaxQuoteResult result)
    {
        Assert.Equal(PtaxQuoteOutcome.Succeeded, result.Outcome);

        return result.Batch!;
    }

    private static PtaxRateClient Client(HttpMessageHandler handler, IRateLimitDelay delay) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://rates.example.test/") }, delay, TimeProvider.System);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rates", name));

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add(path);
            var fixture = path.Contains("EUR", StringComparison.Ordinal)
                ? "ptax-eur-weekend.json"
                : "ptax-usd-weekend.json";

            return Task.FromResult(JsonResponse(Fixture(fixture)));
        }
    }

    private sealed class RateLimitedHandler(TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter ?? TimeSpan.FromSeconds(3));

                return Task.FromResult(limited);
            }

            return Task.FromResult(JsonResponse(Fixture("ptax-usd-weekend.json")));
        }
    }

    private sealed class StatusHandler(HttpStatusCode status, int failures) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(CallCount <= failures
                ? new HttpResponseMessage(status)
                : JsonResponse(Fixture("ptax-usd-weekend.json")));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
    }

    private sealed class EmptyForHandler(string emptyCurrency) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.PathAndQuery.Contains(emptyCurrency, StringComparison.Ordinal)
                ? JsonResponse("""{"value":[]}""")
                : JsonResponse(Fixture("ptax-usd-weekend.json")));
    }

    private sealed class NoCommonDateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isEur = request.RequestUri!.PathAndQuery.Contains("EUR", StringComparison.Ordinal);
            var date = isEur ? "2026-09-04" : "2026-09-03";

            return Task.FromResult(JsonResponse($$"""
                {"value":[{"cotacaoVenda":5.0,"dataHoraCotacao":"{{date}} 13:00:00.000","tipoBoletim":"Fechamento PTAX"}]}
                """));
        }
    }

    private sealed class StubDelay : IRateLimitDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);

            return Task.CompletedTask;
        }
    }

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };
}
