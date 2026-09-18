using System.Net;

namespace ArturRios.Fortuna.Integration.Rates;

/// <summary>Bounded retry rules shared by the outbound clients (PTAX and Pluggy).</summary>
public static class HttpRetryPolicy
{
    /// <summary>Total attempts per request, including the first one.</summary>
    public const int MaximumAttempts = 4;

    /// <summary>A server asking for a longer pause than this is not waited on beyond it.</summary>
    public static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>Explicit per-request timeout for the outbound clients' HttpClient.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// The pause before the next attempt: the server's Retry-After (delta or date) when present,
    /// otherwise exponential backoff, never negative and never above <see cref="MaximumRetryAfter"/>.
    /// </summary>
    public static TimeSpan RetryDelay(HttpResponseMessage response, int attempt, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        var requested = retryAfter?.Delta ??
            (retryAfter?.Date is { } date
                ? date - now
                : TimeSpan.FromSeconds(Math.Pow(2, Math.Clamp(attempt - 1, 0, 10))));
        if (requested < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return requested > MaximumRetryAfter ? MaximumRetryAfter : requested;
    }
}
