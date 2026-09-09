using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class HeimdallAuthGatewayTests
{
    private static readonly Guid ScopeId = Guid.Parse("00000000-0000-0000-0000-000000000076");

    [UnitFact]
    public async Task GivenLoginRequest_WhenForwarded_ThenScopeIsAttachedAndResponseIsMapped()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"data":{"token":"issued","expiresAt":"2026-09-09T13:00:00Z","emailVerified":true,"requiresTwoFactor":false}}
            """));
        var gateway = Gateway(handler);

        var result = await gateway.LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal("issued", result.Data?.Token);
        Assert.Contains($"\"scopeId\":\"{ScopeId}\"", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"email\":\"user@example.test\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"credential\"", handler.Body, StringComparison.Ordinal);
        Assert.Equal("/api/auth/login", handler.Path);
    }

    [UnitFact]
    public async Task GivenTwoFactorChallenge_WhenForwarded_ThenChallengeShapeIsPreserved()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"data":{"token":null,"expiresAt":null,"emailVerified":null,"requiresTwoFactor":true,"challengeToken":"challenge","availableMethods":["App"]}}
            """));
        var gateway = Gateway(handler);

        var result = await gateway.LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.True(result.Data?.RequiresTwoFactor);
        Assert.Equal("challenge", result.Data?.ChallengeToken);
        Assert.Equal(["App"], result.Data?.AvailableMethods);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.BadRequest, HeimdallAuthOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, HeimdallAuthOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, HeimdallAuthOutcome.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, HeimdallAuthOutcome.Unavailable)]
    public async Task GivenUpstreamFailure_WhenForwarded_ThenOnlySafeOutcomeIsReturned(
        HttpStatusCode status,
        HeimdallAuthOutcome expected)
    {
        var handler = new RecordingHandler(_ => Json(status,
            "{\"errors\":[\"sensitive upstream detail\"]}"));

        var result = await Gateway(handler).LoginAsync(
            "user@example.test", "credential", ScopeId, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenHeimdallIsUnreachable_WhenForwarded_ThenUnavailableIsReturned()
    {
        var gateway = Gateway(new ThrowingHandler());

        var result = await gateway.GoogleSignInAsync(
            "id-token", ScopeId, CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Unavailable, result.Outcome);
    }

    [UnitFact]
    public async Task GivenAuthenticatedSignOut_WhenForwarded_ThenBearerTokenIsCarriedOnlyInHeader()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{\"data\":{}}"));

        var result = await Gateway(handler).GoogleSignOutAsync(
            "sensitive-token", CancellationToken.None);

        Assert.Equal(HeimdallAuthOutcome.Succeeded, result.Outcome);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sensitive-token"), handler.Authorization);
        Assert.DoesNotContain("sensitive-token", handler.Body, StringComparison.Ordinal);
    }

    private static HeimdallAuthGateway Gateway(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://heimdall.example.test/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public string Path { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization;
            return response(request);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
