using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging;
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

    /// <summary>What each expected failure's warning says, so that the reason, not just its kind, is logged.</summary>
    private static readonly Dictionary<string, string> Reasons = new()
    {
        ["404"] = "HTTP 404",
        ["unreachable"] = "connection refused",
        ["cut off"] = CutOffStream.Reason,
        ["garbage"] = "The key set is not JSON."
    };

    public static TheoryData<string> Failures() => new()
    {
        "404", "500", "timeout", "unreachable", "cut off", "garbage", "oversized", "no usable key"
    };

    /// <summary>The failures Heimdall or the network can cause, as opposed to a defect in Fortuna.</summary>
    public static TheoryData<string> ExpectedFailures() => new() { "404", "unreachable", "cut off", "garbage" };

    private static (HeimdallJwks Jwks, StubJwksHandler Handler, ManualClock Clock) Create(
        ILogger<HeimdallJwks>? logger = null)
    {
        var handler = new StubJwksHandler();
        var clock = new ManualClock(Start);
        var jwks = new HeimdallJwks(
            new StubHttpClientFactory(handler),
            JwksTestSupport.Options(),
            clock,
            logger ?? NullLogger<HeimdallJwks>.Instance);

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
    public async Task GivenAFetchThatThrowsUnexpectedly_WhenRefreshing_ThenAnErrorWithTheExceptionIsLogged()
    {
        var logger = new CapturingLogger();
        var (jwks, handler, clock) = Create(logger);
        var known = JwksTestSupport.NewKey();
        handler.Serve(JwksTestSupport.KeySet(known));
        await jwks.EnsureKnownAsync(known.Id, CancellationToken.None);
        handler.Respond = _ => throw new InvalidOperationException("a defect in Fortuna");
        clock.Now = Start + HeimdallJwks.MinimumRefreshInterval;

        await jwks.EnsureKnownAsync("a-key-heimdall-just-rotated-to", CancellationToken.None);

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal([known.Id], jwks.Keys.Select(key => key.Id));
    }

    [UnitTheory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task GivenAnExpectedFailure_WhenRefreshing_ThenAWarningWithTheReasonIsLogged(string failure)
    {
        var logger = new CapturingLogger();
        var (jwks, handler, _) = Create(logger);
        handler.Respond = Failing(failure, JwksTestSupport.KeySet(JwksTestSupport.NewKey()));

        await jwks.EnsureKnownAsync("any", CancellationToken.None);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Null(warning.Exception);
        Assert.Contains(Reasons[failure], warning.Message);
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
        "cut off" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CutOffStream())
        }),
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

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>Stands in for the cache's logger and keeps what it was asked to write.</summary>
    private sealed class CapturingLogger : ILogger<HeimdallJwks>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Only what is worth an operator's attention; a successful refresh is Information.
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }

    /// <summary>A body whose connection drops before any byte arrives.</summary>
    private sealed class CutOffStream : Stream
    {
        public const string Reason = "the connection was cut off mid-body";

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException(Reason);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(Reason));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
