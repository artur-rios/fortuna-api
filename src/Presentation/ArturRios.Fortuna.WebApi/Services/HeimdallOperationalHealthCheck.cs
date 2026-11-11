using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;

namespace ArturRios.Fortuna.WebApi.Services;

/// <summary>
///     Whether Heimdall answers at the address, and through the client, that Fortuna's real key fetches use, so
///     a pass means the real dependency works. Optional: a failure degrades the report rather than failing it,
///     since keys already known stay in use. It sends no credential.
/// </summary>
public sealed class HeimdallOperationalHealthCheck(
    IHttpClientFactory clients,
    FortunaOptions options,
    TimeProvider timeProvider) : IOperationalHealthCheck
{
    /// <summary>
    ///     The most a check may take. Here as well as on the internal route because the admin route runs its
    ///     checks one after another under no bound of its own.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    public string Name => "Heimdall";
    public bool IsRequired => false;

    public async Task<OperationalHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        if (options.LocalAuthEnabled)
        {
            return Result(OperationalHealthStatus.NotConfigured);
        }

        using var timeout = new CancellationTokenSource(RequestTimeout, timeProvider);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            using var client = clients.CreateClient(HeimdallJwks.HttpClientName);
            using var response = await client.GetAsync(
                options.HeimdallJwksUri,
                HttpCompletionOption.ResponseHeadersRead,
                bound.Token);

            return response.IsSuccessStatusCode
                ? Result(OperationalHealthStatus.Healthy)
                : Result(OperationalHealthStatus.Unhealthy, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result(OperationalHealthStatus.Unhealthy, HealthReason.From(exception));
        }
    }

    private OperationalHealthCheckResult Result(
        OperationalHealthStatus status,
        string? reason = null) =>
        new(Name, status, IsRequired, Reason: reason);
}
