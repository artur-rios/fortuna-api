using System.Text.Json;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.Services;

namespace ArturRios.Fortuna.WebApi.Observability;

/// <summary>
///     The detailed health report, served only on the metrics port. Traefik routes the application port alone
///     and nothing publishes the metrics port, so yggdrasil's status API reads it without a credential. For the
///     same reason it carries only names, statuses, reason words and the job runner's two figures.
/// </summary>
public static class InternalHealthEndpoint
{
    /// <summary>
    ///     The most one check may take, whether or not it listens for cancellation. The status API gives a
    ///     report 5 seconds, so this has to leave room to answer.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long a report is reused, counted from when its checks finished.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task Handle(HttpContext context)
    {
        try
        {
            var report = await context.RequestServices.GetRequiredService<Reports>().GetAsync(context.RequestAborted);

            context.Response.StatusCode = report.Status == OperationalHealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";

            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                OperationalHealthOutput.From(report),
                Json,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller left. This branch runs ahead of the request logging and the error handling, and a
            // caller must not be able to write to the log by hanging up.
        }
    }

    /// <summary>
    ///     The report, shared by every caller. The endpoint has no credential, so any internal container can
    ///     call it in a loop: a report is kept for <see cref="CacheFor" /> from when its checks finished, and
    ///     callers who arrive during a run wait for it instead of starting their own. The run is on no
    ///     request's cancellation, so a caller who gives up does not cancel it for the rest.
    /// </summary>
    /// <remarks>
    ///     A singleton over checks that are scoped. Each check gets a scope of its own, because they run
    ///     side by side and several can resolve the one <c>AppDbContext</c>, which is not safe concurrently.
    /// </remarks>
    public sealed class Reports(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<Reports> logger)
    {
        private readonly Lock _gate = new();

        // Touched only inside a run, and runs never overlap.
        private readonly Dictionary<string, OperationalHealthStatus> _lastStatus = [];

        private Task<OperationalHealthReport>? _inFlight;
        private OperationalHealthReport? _last;
        private DateTimeOffset _finishedAt;

        internal async Task<OperationalHealthReport> GetAsync(CancellationToken cancellationToken)
        {
            Task<OperationalHealthReport> run;

            lock (_gate)
            {
                if (_inFlight is null && _last is not null && clock.GetUtcNow() - _finishedAt < CacheFor)
                {
                    return _last;
                }

                run = _inFlight ??= Task.Run(RunAsync, CancellationToken.None);
            }

            return await run.WaitAsync(cancellationToken);
        }

        private async Task<OperationalHealthReport> RunAsync()
        {
            OperationalHealthReport? report = null;

            try
            {
                report = await ReportOrFailureAsync();
                LogChanges(report.Services);

                return report;
            }
            finally
            {
                // A finally, so that whatever happens above, the next caller starts a run of its own rather
                // than waiting on this one for good.
                lock (_gate)
                {
                    _inFlight = null;

                    if (report is not null)
                    {
                        _last = report;
                        _finishedAt = clock.GetUtcNow();
                    }
                }
            }
        }

        private async Task<OperationalHealthReport> ReportOrFailureAsync()
        {
            try
            {
                return await RunChecksAsync();
            }
            catch (Exception exception)
            {
                // Only a set of checks that could not be listed gets here, since each check's own failure is
                // caught. A report still goes out.
                return new OperationalHealthReport(
                    OperationalHealthStatus.Unhealthy,
                    [
                        new OperationalHealthCheckResult(
                            "Health checks",
                            OperationalHealthStatus.Unhealthy,
                            true,
                            Reason: HealthReason.From(exception))
                    ]);
            }
        }

        private async Task<OperationalHealthReport> RunChecksAsync()
        {
            var bound = new CancellationTokenSource(CheckTimeout, clock);
            var deadline = bound.Token;
            var running = new List<Task<OperationalHealthCheckResult>>();

            try
            {
                var checks = await ListChecksAsync();

                // Side by side, and started together, so the one deadline is each check's own three seconds
                // and a slow provider costs the others nothing: one after another, two of them at their
                // clients' five seconds would outlast the status API's patience. Task.Run so a check that
                // blocks before its first await cannot hold up the deadline either.
                running.AddRange(checks.Select((_, index) =>
                    Task.Run(() => RunInOwnScopeAsync(index, deadline), CancellationToken.None)));

                var services = await Task.WhenAll(
                    running.Select((task, index) => ResultOfAsync(checks[index], task, deadline)).ToList());

                return new OperationalHealthReport(OperationalHealthEvaluator.Aggregate(services), services);
            }
            finally
            {
                _ = DisposeWhenSettledAsync(bound, running);
            }
        }

        private async Task<IReadOnlyList<CheckIdentity>> ListChecksAsync()
        {
            await using var scope = scopes.CreateAsyncScope();

            return scope.ServiceProvider.GetServices<IOperationalHealthCheck>()
                .Select(check => new CheckIdentity(check.Name, check.IsRequired))
                .ToList();
        }

        /// <summary>
        ///     The check at <paramref name="index" />, in a scope that ends with it, which can be after the report
        ///     has gone out: disposing a DbContext under a query that is still being cancelled can hang or throw,
        ///     and neither may reach the report.
        /// </summary>
        private async Task<OperationalHealthCheckResult> RunInOwnScopeAsync(int index, CancellationToken deadline)
        {
            await using var scope = scopes.CreateAsyncScope();
            var check = scope.ServiceProvider.GetServices<IOperationalHealthCheck>().ElementAt(index);

            return await check.CheckAsync(deadline);
        }

        private static async Task<OperationalHealthCheckResult> ResultOfAsync(
            CheckIdentity check,
            Task<OperationalHealthCheckResult> running,
            CancellationToken deadline)
        {
            try
            {
                // WaitAsync as well as the token the check was given: a dependency that hangs in a place that
                // does not watch the token is exactly the case the bound is for.
                return await running.WaitAsync(deadline);
            }
            catch (Exception exception)
            {
                return new OperationalHealthCheckResult(
                    check.Name,
                    OperationalHealthStatus.Unhealthy,
                    check.IsRequired,
                    Reason: HealthReason.From(exception));
            }
        }

        private static async Task DisposeWhenSettledAsync(
            CancellationTokenSource bound,
            List<Task<OperationalHealthCheckResult>> running)
        {
            await Task.WhenAll(running.Cast<Task>()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            bound.Dispose();
        }

        // Once per change of status, and only the reason word: a flood of requests cannot flood the log, and
        // no stack trace is written for a dependency being down.
        private void LogChanges(IEnumerable<OperationalHealthCheckResult> services)
        {
            foreach (var service in services)
            {
                var changed = !_lastStatus.TryGetValue(service.Name, out var previous) ||
                              previous != service.Status;
                _lastStatus[service.Name] = service.Status;

                if (changed && service.Status is OperationalHealthStatus.Degraded or OperationalHealthStatus.Unhealthy)
                {
                    // The checks that catch their own failures report none, so their status stands in for it.
                    logger.LogWarning(
                        "{Service} failed its health check: {Reason}",
                        service.Name,
                        service.Reason ?? service.Status.ToString());
                }
            }
        }

        private sealed record CheckIdentity(string Name, bool IsRequired);
    }
}
