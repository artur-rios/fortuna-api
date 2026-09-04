using System.Net;
using System.Net.Http.Headers;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class PtaxRateClientTests
{
    [UnitFact]
    public async Task GivenWeekendWithoutPublication_WhenRatesAreRead_ThenLatestCommonClosingPublicationIsUsed()
    {
        var handler = new FixtureHandler();
        var client = Client(handler, new StubDelay());

        var result = await client.GetLatestQuotesAsync(
            ["EUR", "USD"],
            new DateOnly(2026, 9, 6),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 4), result.PublicationDate);
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

        var result = await client.GetLatestQuotesAsync(
            ["USD"],
            new DateOnly(2026, 9, 4),
            CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal([TimeSpan.FromSeconds(3)], delay.Delays);
        Assert.Equal(5.12m, Assert.Single(result.Quotes).BrlPerUnit);
    }

    [UnitFact]
    public async Task GivenNoCommonPriorPublication_WhenRatesAreRead_ThenActionableFailureIsRaised()
    {
        var client = Client(new NoCommonDateHandler(), new StubDelay());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetLatestQuotesAsync(
                ["EUR", "USD"],
                new DateOnly(2026, 9, 4),
                CancellationToken.None));

        Assert.Equal(ExchangeRateSyncMessages.PublicationUnavailable, exception.Message);
    }

    private static PtaxRateClient Client(HttpMessageHandler handler, IRateLimitDelay delay) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://rates.example.test/") }, delay);

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

    private sealed class RateLimitedHandler : HttpMessageHandler
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
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
                return Task.FromResult(limited);
            }

            return Task.FromResult(JsonResponse(Fixture("ptax-usd-weekend.json")));
        }
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
