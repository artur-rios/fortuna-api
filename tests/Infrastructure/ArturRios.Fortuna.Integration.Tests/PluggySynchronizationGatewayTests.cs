using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class PluggySynchronizationGatewayTests
{
    [UnitFact]
    public async Task GivenAccountsAndTransactions_WhenFetched_ThenBatchIsNormalized()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.OK, "{\"connector\":{\"name\":\"Nubank\"}}"),
            Response(HttpStatusCode.OK, """
                {"total":2,"results":[
                  {"id":"account-1","type":"BANK","name":"Checking","currencyCode":"BRL","balance":100},
                  {"id":"card-1","type":"CREDIT","name":"Card","number":"12345678","currencyCode":"BRL","creditData":{"creditLimit":5000,"balanceCloseDate":"2026-09-10","balanceDueDate":"2026-09-17"}}
                ]}
                """),
            Response(HttpStatusCode.OK, """
                {"total":1,"results":[{"id":"transaction-1","accountId":"account-1","type":"DEBIT","amount":-42.50,"date":"2026-08-14T12:00:00Z","description":"Lunch","category":{"description":"Food"}}]}
                """),
            Response(HttpStatusCode.OK, """
                {"total":1,"results":[{"id":"transaction-2","accountId":"card-1","type":"CREDIT","amount":12,"date":"2026-08-15","description":"Refund","category":"Shopping"}]}
                """));

        var result = await Gateway(handler).FetchAsync(
            "item-1",
            "secret-token",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        Assert.Equal(PluggySynchronizationFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Batch?.Resources.Count);
        Assert.Equal(2, result.Batch?.Transactions.Count);
        var card = result.Batch!.Resources.Single(item => item.Kind == PluggyResourceKind.CreditCard);
        Assert.Equal("Nubank", card.Institution);
        Assert.Equal("5678", card.LastFourDigits);
        Assert.Equal((short)10, card.ClosingDay);
        Assert.Equal((short)17, card.DueDay);
        var expense = result.Batch.Transactions.Single(item => item.ExternalReference == "transaction-1");
        Assert.Equal(TransactionDirection.Expense, expense.Direction);
        Assert.Equal(42.50m, expense.Amount);
        Assert.Equal("Food", expense.Category);
        Assert.Contains("\"id\":\"transaction-1\"", expense.RawPayload, StringComparison.Ordinal);
        Assert.All(handler.ApiKeys, key => Assert.Equal("secret-token", key));
        Assert.Contains(handler.Paths, path => path.Contains(
            "from=2026-08-01&to=2026-08-31", StringComparison.Ordinal));
    }

    [UnitFact]
    public async Task GivenRateLimit_WhenFetched_ThenRequestBacksOffAndResumes()
    {
        var rateLimit = Response(HttpStatusCode.TooManyRequests, "{}");
        rateLimit.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        var handler = new SequenceHandler(
            rateLimit,
            Response(HttpStatusCode.OK, "{\"connector\":{\"name\":\"Bank\"}}"),
            Response(HttpStatusCode.OK, "{\"total\":0,\"results\":[]}"));
        var delay = new StubDelay();

        var result = await Gateway(handler, delay).FetchAsync(
            "item-1", "token", null, null, CancellationToken.None);

        Assert.Equal(PluggySynchronizationFetchOutcome.Succeeded, result.Outcome);
        Assert.Equal([TimeSpan.FromSeconds(3)], delay.Delays);
        Assert.Equal(3, handler.Paths.Count);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GivenExpiredConnection_WhenFetched_ThenReauthenticationIsRequired(
        HttpStatusCode status)
    {
        var result = await Gateway(new SequenceHandler(Response(status, "{}"))).FetchAsync(
            "item-1", "token", null, null, CancellationToken.None);

        Assert.Equal(PluggySynchronizationFetchOutcome.RequiresReauthentication, result.Outcome);
    }

    [UnitFact]
    public async Task GivenMalformedResponse_WhenFetched_ThenSourceIsUnavailable()
    {
        var result = await Gateway(new SequenceHandler(Response(HttpStatusCode.OK, "not-json")))
            .FetchAsync("item-1", "token", null, null, CancellationToken.None);

        Assert.Equal(PluggySynchronizationFetchOutcome.Unavailable, result.Outcome);
    }

    private static PluggySynchronizationGateway Gateway(
        HttpMessageHandler handler,
        IRateLimitDelay? delay = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://pluggy.example/") },
        new PluggySourceOptions(
            "client", "secret", new Uri("https://pluggy.example/"), true),
        delay ?? new StubDelay());

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<string> ApiKeys { get; } = [];
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            ApiKeys.Add(request.Headers.GetValues("X-API-KEY").Single());
            return Task.FromResult(queue.Dequeue());
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
}
