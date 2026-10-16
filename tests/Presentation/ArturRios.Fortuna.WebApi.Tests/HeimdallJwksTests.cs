using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// The cache of Heimdall's published keys: loaded when a token names an unknown key, fetched at most once a
/// minute however many requests ask, and kept as it is when a fetch fails in any way.
/// </summary>
public sealed class HeimdallJwksTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Long enough for any fetch to end, its own timeout included; a hang fails instead of stalling.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public static TheoryData<string> Failures() => new()
    {
        "404", "500", "timeout", "unreachable", "garbage", "oversized", "no usable key"
    };

    private static (HeimdallJwks Jwks, StubJwksHandler Handler, ManualClock Clock) Create()
    {
        var handler = new StubJwksHandler();
        var clock = new ManualClock(Start);
        var jwks = new HeimdallJwks(
            new StubHttpClientFactory(handler), JwksTestSupport.Options(), clock, NullLogger<HeimdallJwks>.Instance);

        return (jwks, handler, clock);
    }

    [UnitFact]
    public async Task GivenAnUnknownKey_WhenRefreshing_ThenThePublishedKeysAreLoaded()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        handler.Serve(JwksTestSupport.KeySet(key));

        await jwks.EnsureKnownAsync(key.Id, CancellationToken.None);

        Assert.Equal([key.Id], jwks.Keys.Select(known => known.Id));
        Assert.All(jwks.Keys, known => Assert.False(known.CanSign));
        Assert.Equal(1, handler.Requests);
    }

    [UnitFact]
    public async Task GivenAKnownKey_WhenAskedForAgain_ThenNothingIsFetched()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        handler.Serve(JwksTestSupport.KeySet(key));
        await jwks.EnsureKnownAsync(key.Id, CancellationToken.None);

        await jwks.EnsureKnownAsync(key.Id, CancellationToken.None);

        Assert.Equal(1, handler.Requests);
    }

    [UnitFact]
    public async Task GivenARefreshWithinTheLastMinute_WhenAnotherKeyIsUnknown_ThenNothingIsFetched()
    {
        var (jwks, handler, clock) = Create();
        handler.Serve(JwksTestSupport.KeySet(JwksTestSupport.NewKey()));
        await jwks.EnsureKnownAsync("invented-1", CancellationToken.None);

        clock.Now = Start + TimeSpan.FromSeconds(59);
        await jwks.EnsureKnownAsync("invented-2", CancellationToken.None);

        Assert.Equal(1, handler.Requests);

        clock.Now = Start + HeimdallJwks.MinimumRefreshInterval;
        await jwks.EnsureKnownAsync("invented-3", CancellationToken.None);

        Assert.Equal(2, handler.Requests);
    }

    [UnitFact]
    public async Task GivenConcurrentRequestsForAnUnknownKey_WhenRefreshing_ThenOneFetchServesThemAll()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async _ =>
        {
            await gate.Task;

            return StubJwksHandler.Json(JwksTestSupport.KeySet(key));
        };

        var waiting = Enumerable.Range(0, 20)
            .Select(_ => jwks.EnsureKnownAsync(key.Id, CancellationToken.None))
            .ToList();
        gate.SetResult();
        await Task.WhenAll(waiting).WaitAsync(TestTimeout);

        Assert.Equal(1, handler.Requests);
        Assert.Contains(jwks.Keys, known => known.Id == key.Id);
    }

    [UnitFact]
    public async Task GivenAWaitingRequestIsCancelled_WhenTheSharedFetchCompletes_ThenTheKeysAreRefreshed()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCancelled = false;
        handler.Respond = async cancellationToken =>
        {
            started.SetResult();
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                fetchCancelled = true;

                throw;
            }

            return StubJwksHandler.Json(JwksTestSupport.KeySet(key));
        };

        using var aborted = new CancellationTokenSource();
        var abandoning = jwks.EnsureKnownAsync(key.Id, aborted.Token);
        await started.Task.WaitAsync(TestTimeout);
        await aborted.CancelAsync();
        await abandoning.WaitAsync(TestTimeout);
        var staying = jwks.EnsureKnownAsync(key.Id, CancellationToken.None);
        gate.SetResult();
        await staying.WaitAsync(TestTimeout);

        Assert.False(fetchCancelled);
        Assert.Equal(1, handler.Requests);
        Assert.Equal([key.Id], jwks.Keys.Select(known => known.Id));
    }

    [UnitFact]
    public async Task GivenAKeySetInAnUnknownCharset_WhenRefreshing_ThenItIsStillRead()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        handler.Respond = _ =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JwksTestSupport.KeySet(key)));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=nonsense");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };

        await jwks.EnsureKnownAsync(key.Id, CancellationToken.None);

        Assert.Equal([key.Id], jwks.Keys.Select(known => known.Id));
    }

    [UnitFact]
    public async Task GivenAKeySetOfExactlyTheLimit_WhenRefreshing_ThenItIsRead()
    {
        var (jwks, handler, _) = Create();
        var key = JwksTestSupport.NewKey();
        handler.Serve(Padded(JwksTestSupport.KeySet(key), HeimdallJwks.MaximumDocumentBytes));

        await jwks.EnsureKnownAsync(key.Id, CancellationToken.None);

        Assert.Equal([key.Id], jwks.Keys.Select(known => known.Id));
    }

    [UnitTheory]
    [MemberData(nameof(Failures))]
    public async Task GivenAFetchThatFails_WhenRefreshing_ThenTheKnownKeysStay(string failure)
    {
        var (jwks, handler, clock) = Create();
        var known = JwksTestSupport.NewKey();
        handler.Serve(JwksTestSupport.KeySet(known));
        await jwks.EnsureKnownAsync(known.Id, CancellationToken.None);
        var rotated = JwksTestSupport.NewKey();
        handler.Respond = Failing(failure, JwksTestSupport.KeySet(rotated));
        clock.Now = Start + HeimdallJwks.MinimumRefreshInterval;

        await jwks.EnsureKnownAsync(rotated.Id, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(2, handler.Requests);
        Assert.Equal([known.Id], jwks.Keys.Select(key => key.Id));

        await jwks.EnsureKnownAsync(rotated.Id, CancellationToken.None);

        Assert.Equal(2, handler.Requests);
    }

    [UnitFact]
    public async Task GivenHeimdallPublishesNothingYet_WhenRefreshing_ThenNoKeyIsKnownAndNothingThrows()
    {
        // Until Heimdall publishes a key set its endpoint answers an error status; the stub's default is 404.
        var (jwks, handler, _) = Create();

        await jwks.EnsureKnownAsync("any", CancellationToken.None);

        Assert.Empty(jwks.Keys);
        Assert.Equal(1, handler.Requests);
    }

    /// <summary>
    /// Each failure carries the rotated key wherever it can, so that only the guard it is about keeps the key
    /// out: an error status with a valid key set as its body, and a valid key set made too large by leading
    /// whitespace, which JSON allows.
    /// </summary>
    private static Func<CancellationToken, Task<HttpResponseMessage>> Failing(string failure, string keySet) => failure switch
    {
        "404" => _ => Task.FromResult(WithStatus(HttpStatusCode.NotFound, keySet)),
        "500" => _ => Task.FromResult(WithStatus(HttpStatusCode.InternalServerError, keySet)),
        "timeout" => async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return StubJwksHandler.Json(keySet);
        },
        "unreachable" => _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")),
        "garbage" => _ => Task.FromResult(StubJwksHandler.Json("<html>not a key set</html>")),
        "oversized" => _ => Task.FromResult(StubJwksHandler.Json(Padded(keySet, HeimdallJwks.MaximumDocumentBytes + 1))),
        _ => _ => Task.FromResult(StubJwksHandler.Json("{\"keys\":[{\"kty\":\"RSA\",\"n\":\"AQAB\",\"e\":\"AQAB\"}]}"))
    };

    private static HttpResponseMessage WithStatus(HttpStatusCode status, string body)
    {
        var response = StubJwksHandler.Json(body);
        response.StatusCode = status;

        return response;
    }

    /// <summary><paramref name="keySet" /> behind enough leading spaces to make it <paramref name="bytes" /> long.</summary>
    private static string Padded(string keySet, int bytes) =>
        new string(' ', bytes - Encoding.UTF8.GetByteCount(keySet)) + keySet;
}
