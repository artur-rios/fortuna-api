using System.Diagnostics;
using System.Net;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Util.Test.Attributes;
using ArturRios.Util.WebApi.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// Every request's start is logged with its client IP address by Util.WebApi's
/// <see cref="TraceActivityMiddleware" />, which also tags the address on the request's activity as
/// <c>client.address</c>. Which address that is depends on the forwarded-header trust: behind a
/// configured proxy it is the caller's, and a connection outside it cannot choose its own.
/// </summary>
/// <remarks>
/// These run the real pipeline. The test server has no socket, so a startup filter stamps the
/// address a real connection would have carried — the proxy's, or a stranger's — ahead of it.
/// No test here reaches the database.
/// </remarks>
public sealed class ClientAddressLoggingTests
{
    private const string TrustedNetwork = "10.0.0.0/8";
    private const string Proxy = "10.1.2.3";
    private const string Caller = "203.0.113.7";
    private const string Stranger = "198.51.100.20";
    private const string ClientAddressTag = "client.address";

    [FunctionalFact]
    public async Task GivenAnyRequest_WhenItIsServed_ThenItsStartIsLoggedWithTheClientAddress()
    {
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Caller);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthcheck");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("traceparent"));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(Caller, entry.ClientIp);
        Assert.Contains(Caller, entry.Message, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenRequestsThatNeverReachAnAction_WhenServed_ThenEachIsStillLogged()
    {
        // Anonymous, refused and unrouted requests are logged alike: the entry is written before
        // authentication or rate limiting can short-circuit the request (the fallback policy
        // answers an unknown path 401 too).
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Caller);
        using var client = factory.CreateClient();

        using var anonymous = await client.GetAsync("/healthcheck");
        using var unauthenticated = await client.GetAsync("/api/me");
        using var unknown = await client.GetAsync("/no-such-path");

        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(3, log.Entries.Count);
        Assert.All(log.Entries, entry => Assert.Equal(Caller, entry.ClientIp));
    }

    [FunctionalFact]
    public async Task GivenAnyRequest_WhenItIsServed_ThenTheClientAddressIsTaggedOnItsActivity()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener();
        listener.ShouldListenTo = source => source.Name == "Microsoft.AspNetCore";
        listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllDataAndRecorded;
        listener.ActivityStopped = activity =>
        {
            lock (stopped)
            {
                stopped.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        await using var factory = CreateFactory(new CapturingLogger(), remoteAddress: Caller);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthcheck");

        var traceId = ActivityContext.Parse(response.Headers.GetValues("traceparent").Single(), null).TraceId;
        Activity request;
        lock (stopped)
        {
            request = Assert.Single(stopped, activity => activity.TraceId == traceId);
        }

        Assert.Equal(Caller, request.GetTagItem(ClientAddressTag));
    }

    [FunctionalFact]
    public async Task GivenPrivateMetricsListener_WhenPrometheusScrapes_ThenTheScrapeIsNotLogged()
    {
        // The exporter answers ahead of the trace middleware, so scrapes add nothing to the log.
        var log = new CapturingLogger();
        await using var factory = CreateFactory(
            log,
            remoteAddress: Proxy,
            localPort: FortunaOptions.DefaultMetricsPort);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(PrometheusMetrics.ScrapePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(log.Entries);
    }

    [FunctionalFact]
    public async Task GivenTrustedProxy_WhenItForwardsACaller_ThenTheCallerAddressIsLogged()
    {
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Proxy, forwardedNetworks: TrustedNetwork);
        using var client = factory.CreateClient();

        using var response = await SendForwardedAsync(client, forwardedFor: Caller);

        Assert.Equal(Caller, Assert.Single(log.Entries).ClientIp);
    }

    [FunctionalFact]
    public async Task GivenTrustedProxy_WhenTheCallerForgedAnEarlierHop_ThenOnlyTheHopTheProxySawIsLogged()
    {
        // The proxy appends the address it saw to whatever the caller sent. Only that last entry
        // is vouched for; the forged one ahead of it must not be logged as the client.
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Proxy, forwardedNetworks: TrustedNetwork);
        using var client = factory.CreateClient();

        using var response = await SendForwardedAsync(client, forwardedFor: $"{Stranger}, {Caller}");

        Assert.Equal(Caller, Assert.Single(log.Entries).ClientIp);
    }

    [FunctionalFact]
    public async Task GivenUntrustedConnection_WhenItSendsForwardedFor_ThenTheConnectionAddressIsLogged()
    {
        // A caller reaching the API directly cannot rename itself in the log.
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Stranger, forwardedNetworks: TrustedNetwork);
        using var client = factory.CreateClient();

        using var response = await SendForwardedAsync(client, forwardedFor: Caller);

        Assert.Equal(Stranger, Assert.Single(log.Entries).ClientIp);
    }

    [FunctionalFact]
    public async Task GivenNoTrustedProxyConfigured_WhenAProxyForwardsACaller_ThenTheProxyAddressIsLogged()
    {
        var log = new CapturingLogger();
        await using var factory = CreateFactory(log, remoteAddress: Proxy);
        using var client = factory.CreateClient();

        using var response = await SendForwardedAsync(client, forwardedFor: Caller);

        Assert.Equal(Proxy, Assert.Single(log.Entries).ClientIp);
    }

    private static Task<HttpResponseMessage> SendForwardedAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthcheck");
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        CapturingLogger log,
        string remoteAddress,
        string? forwardedNetworks = null,
        int? localPort = null)
    {
        foreach (var setting in ValidSettings(forwardedNetworks))
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IStartupFilter>(
                    new ConnectionStartupFilter(IPAddress.Parse(remoteAddress), localPort));
                services.AddSingleton<ILogger<TraceActivityMiddleware>>(log);
            });
        });
    }

    private static Dictionary<string, string?> ValidSettings(string? forwardedNetworks) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = bool.FalseString,
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = null,
        ["FORTUNA_RATES_SYNC_CRON"] = null,
        ["FORTUNA_RATES_CURRENCIES"] = null,
        ["FORTUNA_FORWARDED_KNOWN_NETWORKS"] = forwardedNetworks,
        ["FORTUNA_FORWARDED_KNOWN_PROXIES"] = null,
        ["FORTUNA_METRICS_PORT"] = null
    };

    private sealed record LogEntry(LogLevel Level, string Message, string? ClientIp);

    /// <summary>Stands in for the middleware's logger and keeps what it was asked to write.</summary>
    private sealed class CapturingLogger : ILogger<TraceActivityMiddleware>
    {
        private readonly List<LogEntry> entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (entries)
                {
                    return entries.ToArray();
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
            // Only the request-start entry; the end-of-request trace entry carries no address.
            if (logLevel < LogLevel.Information)
            {
                return;
            }

            var clientIp = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.FirstOrDefault(value => value.Key == "ClientIp").Value?.ToString()
                : null;

            lock (entries)
            {
                entries.Add(new LogEntry(logLevel, formatter(state, exception), clientIp));
            }
        }
    }

    /// <summary>
    /// The test server has no socket, so the connection's remote address — and, for the metrics
    /// listener, its local port — is set explicitly.
    /// </summary>
    private sealed class ConnectionStartupFilter(IPAddress remoteAddress, int? localPort) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteAddress;
                if (localPort is not null)
                {
                    context.Connection.LocalPort = localPort.Value;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
