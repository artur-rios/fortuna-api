using System.Net;
using System.Text;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class PluggyConnectionGatewayTests
{
    private const string ItemId = "1b0f3cd5-c902-4836-b68b-04cbd847f99a";

    [UnitFact]
    public async Task GivenValidItem_WhenValidated_ThenInstitutionAndAccessTokenAreReturned()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.OK, "{\"accessToken\":\"api-key\"}"),
            Response(HttpStatusCode.OK,
                $"{{\"id\":\"{ItemId}\",\"connector\":{{\"name\":\"Nubank\"}}}}"));

        var result = await Client(handler).ValidateAsync(ItemId, CancellationToken.None);

        Assert.Equal(PluggyConnectionValidationOutcome.Succeeded, result.Outcome);
        Assert.Equal("Nubank", result.Institution);
        Assert.Equal("api-key", result.AccessToken);
        Assert.Equal("api-key", handler.ItemApiKey);
        Assert.DoesNotContain("api-key", handler.ItemUri?.ToString(), StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GivenUnknownItem_WhenValidated_ThenReferenceIsInvalid(HttpStatusCode status)
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.OK, "{\"accessToken\":\"api-key\"}"),
            Response(status, "{}"));

        var result = await Client(handler).ValidateAsync(ItemId, CancellationToken.None);

        Assert.Equal(PluggyConnectionValidationOutcome.InvalidReference, result.Outcome);
    }

    [UnitFact]
    public async Task GivenInvalidApplicationCredentials_WhenValidated_ThenSourceIsNotConfigured()
    {
        var result = await Client(new SequenceHandler(
            Response(HttpStatusCode.Unauthorized, "{}")))
            .ValidateAsync(ItemId, CancellationToken.None);

        Assert.Equal(PluggyConnectionValidationOutcome.NotConfigured, result.Outcome);
    }

    [UnitFact]
    public async Task GivenNetworkOrMalformedResponse_WhenValidated_ThenSourceIsUnavailable()
    {
        var network = await Client(new ThrowingHandler())
            .ValidateAsync(ItemId, CancellationToken.None);
        var malformed = await Client(new SequenceHandler(
            Response(HttpStatusCode.OK, "not-json")))
            .ValidateAsync(ItemId, CancellationToken.None);

        Assert.Equal(PluggyConnectionValidationOutcome.Unavailable, network.Outcome);
        Assert.Equal(PluggyConnectionValidationOutcome.Unavailable, malformed.Outcome);
    }

    [UnitTheory]
    [InlineData(false, "client", "secret", PluggyConnectionValidationOutcome.Unavailable)]
    [InlineData(true, null, "secret", PluggyConnectionValidationOutcome.NotConfigured)]
    [InlineData(true, "client", null, PluggyConnectionValidationOutcome.NotConfigured)]
    public async Task GivenUnavailableConfiguration_WhenValidated_ThenNoRequestIsSent(
        bool network,
        string? clientId,
        string? clientSecret,
        PluggyConnectionValidationOutcome expected)
    {
        var handler = new SequenceHandler();
        var gateway = new PluggyConnectionGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://pluggy.example/") },
            new PluggySourceOptions(clientId, clientSecret, new Uri("https://pluggy.example/"), network));

        var result = await gateway.ValidateAsync(ItemId, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, handler.RequestCount);
    }

    private static PluggyConnectionGateway Client(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://pluggy.example/") },
        new PluggySourceOptions(
            "client", "secret", new Uri("https://pluggy.example/"), true));

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public int RequestCount { get; private set; }
        public string? ItemApiKey { get; private set; }
        public Uri? ItemUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Method == HttpMethod.Get)
            {
                ItemUri = request.RequestUri;
                ItemApiKey = request.Headers.GetValues("X-API-KEY").Single();
            }

            return Task.FromResult(queue.Dequeue());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Pluggy is unreachable.");
    }
}
