using System.Text;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Jwt;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Heimdall's published ES256 verification keys: fetched from <c>FORTUNA_HEIMDALL_JWKS_URL</c> when a token
/// names a key Fortuna does not know, at most once a minute, and kept between fetches.
/// </summary>
/// <remarks>
/// <para>
/// <b>A failure keeps what is known.</b> A fetch that fails, takes longer than <see cref="FetchTimeout" />,
/// answers anything but 2xx, sends more than <see cref="MaximumDocumentBytes" />, or holds no usable key
/// changes nothing: the keys already known stay in use, and the reason is logged as a warning (any other
/// exception is a defect, logged as an error with its stack trace). While no key is known, no Heimdall
/// token validates: Heimdall's retired HS256 tokens are refused whatever the key set holds.
/// </para>
/// <para>
/// <b>Once a minute.</b> A token naming an unknown key triggers a fetch only when none was attempted in the
/// last <see cref="MinimumRefreshInterval" />, and concurrent requests wait for one fetch rather than each
/// making their own, so a flood of tokens with invented key ids costs Heimdall one request a minute.
/// </para>
/// <para>
/// <b>The fetch belongs to no request.</b> It runs under its own <see cref="FetchTimeout" />, never a
/// caller's token: a request that stops waiting (its client went away) neither cancels the fetch the
/// others are waiting for nor spends the minute's attempt on nothing.
/// </para>
/// </remarks>
public sealed class HeimdallJwks(
    IHttpClientFactory httpClientFactory,
    FortunaOptions options,
    TimeProvider timeProvider,
    ILogger<HeimdallJwks> logger)
{
    /// <summary>The named HTTP client the key set is fetched with. It sends no caller headers.</summary>
    public const string HttpClientName = "HeimdallJwks";

    /// <summary>The largest key set read, in bytes. Heimdall's, with two keys, is under 500.</summary>
    public const int MaximumDocumentBytes = 64 * 1024;

    /// <summary>The shortest time between two fetches.</summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>How long a fetch, the body included, may take before it counts as failed.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private IReadOnlyList<JwtKey> _keys = [];
    private DateTimeOffset? _lastAttempt;
    private Task _fetch = Task.CompletedTask;

    /// <summary>The keys known now: public halves only.</summary>
    public IReadOnlyList<JwtKey> Keys => Volatile.Read(ref _keys);

    /// <summary>
    /// Makes sure the key <paramref name="keyId" /> names is known, fetching the key set when it is not and
    /// no fetch was attempted in the last minute, or waiting for the fetch already under way.
    /// </summary>
    /// <remarks>
    /// Never throws: a failed fetch keeps the known keys, and when <paramref name="cancellationToken" /> is
    /// cancelled this returns without waiting further while the fetch carries on for everyone else.
    /// </remarks>
    public async Task EnsureKnownAsync(string keyId, CancellationToken cancellationToken)
    {
        if (Knows(keyId))
        {
            return;
        }

        Task fetch;

        lock (_gate)
        {
            if (_fetch.IsCompleted)
            {
                var now = timeProvider.GetUtcNow();

                if (Knows(keyId) || (_lastAttempt is { } last && now - last < MinimumRefreshInterval))
                {
                    return;
                }

                _lastAttempt = now;
                _fetch = Task.Run(FetchAsync, CancellationToken.None);
            }

            fetch = _fetch;
        }

        try
        {
            await fetch.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // This caller stopped waiting; the fetch goes on for the others.
        }
    }

    private bool Knows(string keyId) => Keys.Any(key => key.Id == keyId);

    /// <summary>Fetches the key set and, when it holds a usable key, replaces the known keys. Never throws.</summary>
    private async Task FetchAsync()
    {
        using var timeout = new CancellationTokenSource(FetchTimeout);

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                options.HeimdallJwksUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                Kept($"HTTP {(int)response.StatusCode}");

                return;
            }

            if (await ReadAsync(response.Content, timeout.Token) is not { } body)
            {
                Kept($"the key set is larger than {MaximumDocumentBytes} bytes");

                return;
            }

            var keys = JwksDocument.Parse(body);

            if (keys.Count == 0)
            {
                Kept("the key set holds no usable ES256 key");

                return;
            }

            Volatile.Write(ref _keys, keys);
            logger.LogInformation(
                "Heimdall's signing keys refreshed from {JwksUrl}: {KeyIds}",
                options.HeimdallJwksUri, string.Join(", ", keys.Select(key => key.Id)));
        }
        catch (OperationCanceledException)
        {
            // The fetch's own token is the only one it runs under, so a cancellation is its timeout.
            Kept($"no answer within {FetchTimeout}");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException)
        {
            // What the network or Heimdall can cause: unreachable, cut off mid-body, or not a key set. None of
            // these messages quotes the body (JwksDocument's FormatException messages are fixed text).
            Kept(exception.Message);
        }
        catch (Exception exception)
        {
            // Anything else is a defect (a misconfigured client, a library regression), not Heimdall being
            // absent. The request path still must not see it, but it is logged as an error with its stack
            // trace so that it does not hide among the warnings expected until Heimdall publishes keys.
            logger.LogError(
                exception,
                "Heimdall's signing keys could not be refreshed from {JwksUrl} because of an unexpected error. The keys already known stay in use.",
                options.HeimdallJwksUri);
        }
    }

    /// <summary>
    /// The body decoded as UTF-8 whatever charset it claims, so an unknown one is just another body to parse,
    /// or <see langword="null" /> when it is larger than <see cref="MaximumDocumentBytes" />.
    /// </summary>
    private static async Task<string?> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumDocumentBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length > MaximumDocumentBytes ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }

    private void Kept(string reason) => logger.LogWarning(
        "Heimdall's signing keys were not refreshed from {JwksUrl}: {Reason}. The keys already known stay in use.",
        options.HeimdallJwksUri, reason);
}
