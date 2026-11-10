using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// The report on the metrics port, and the port itself. The in-memory server has no sockets, so these hosts are
/// built from the calls the application makes, with one middleware in front that stamps the local port a real
/// Kestrel connection would have carried, and a terminal step standing in for the rest of the API. That step
/// marks its response, which is how a test tells "the port refused it" from "the API answered".
/// </summary>
public sealed class InternalHealthTests
{
    private const int MetricsPort = 9464;
    private const int ApplicationPort = 8080;
    private const string Route = "/internal/health";
    private const string ReachedTheApi = "X-Reached-The-Api";
    private const string Secret = "secret-detail-that-must-not-leak";

    // Written out rather than read from the endpoint, so that these tests pin the values themselves.
    private static readonly TimeSpan CheckBound = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(5);

    /// <summary>Long enough for anything that should end; a hang fails instead of stalling.</summary>
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    [UnitFact]
    public async Task GivenHeimdallAnswers2xx_WhenInternalHealthRequested_ThenHeimdallHealthy()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, RealHeimdall(HeimdallStub.Answering()));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"status":"Healthy","services":[{"name":"Heimdall","status":"Healthy"}]}""",
            body);
    }

    [UnitTheory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    [InlineData(HttpStatusCode.Found, "HTTP 302")]
    public async Task GivenHeimdallAnswersOutsideTwoHundreds_WhenInternalHealthRequested_ThenDegradedWithHttpReason(
        HttpStatusCode status,
        string reason)
    {
        // Given
        await using var app = await StartAsync(MetricsPort, RealHeimdall(HeimdallStub.Answering(status)));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $$"""{"status":"Degraded","services":[{"name":"Heimdall","status":"Unhealthy","reason":"{{reason}}"}]}""",
            body);
    }

    [UnitFact]
    public async Task GivenHeimdallRefused_WhenInternalHealthRequested_ThenDegraded200WithReason()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, RealHeimdall(HeimdallStub.Refusing()));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"status":"Degraded","services":[{"name":"Heimdall","status":"Unhealthy","reason":"connection refused"}]}""",
            body);
        Assert.DoesNotContain(Secret, body);
    }

    [UnitFact]
    public async Task GivenLocalAuthEnabled_WhenInternalHealthRequested_ThenHeimdallNotConfiguredAndNoCallMade()
    {
        // Given
        var heimdall = HeimdallStub.Answering();
        var options = JwksTestSupport.Options() with { LocalAuthEnabled = true };
        await using var app = await StartAsync(MetricsPort, RealHeimdall(heimdall, options));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then not configured does not count against the report
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"status":"Healthy","services":[{"name":"Heimdall","status":"NotConfigured"}]}""",
            body);
        Assert.Empty(heimdall.Requests);
    }

    [UnitFact]
    public async Task GivenHeimdallCheck_WhenItRuns_ThenItSendsOneBareGetToTheKeySetAddress()
    {
        // Given
        var heimdall = HeimdallStub.Answering();
        await using var app = await StartAsync(MetricsPort, RealHeimdall(heimdall));

        // When
        await app.GetTestClient().GetAsync(Route);

        // Then the address real key fetches use, with nothing that could carry a credential
        var request = Assert.Single(heimdall.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(JwksTestSupport.JwksUrl, request.Uri);
        Assert.Null(request.Authorization);
        Assert.False(request.HasBody);
    }

    [UnitFact]
    public void GivenHeimdallCheck_WhenInspected_ThenItIsNamedAndOptional()
    {
        // Given
        var check = new HeimdallOperationalHealthCheck(
            new StubHttpClientFactory(HeimdallStub.Answering()), JwksTestSupport.Options(), TimeProvider.System);

        // When
        var (name, required) = (check.Name, check.IsRequired);

        // Then
        Assert.Equal("Heimdall", name);
        Assert.False(required);
    }

    [UnitFact]
    public async Task GivenHeimdallNeverAnswers_WhenItsOwnBoundPasses_ThenTheCheckReadsTimeout()
    {
        // Given a Heimdall that listens for cancellation and nothing else, and a caller with no deadline, as
        // the admin route's sequential evaluation has
        var clock = new TimerClock();
        var heimdall = HeimdallStub.Hanging();
        var check = new HeimdallOperationalHealthCheck(
            new StubHttpClientFactory(heimdall), JwksTestSupport.Options(), clock);
        var running = check.CheckAsync(CancellationToken.None);
        await heimdall.Started.Task.WaitAsync(Soon);

        // When
        clock.Advance(CheckBound);
        var result = await running.WaitAsync(Soon);

        // Then
        Assert.Equal(OperationalHealthStatus.Unhealthy, result.Status);
        Assert.Equal("timeout", result.Reason);
    }

    [UnitFact]
    public async Task GivenCallerCancels_WhenHeimdallCheckRuns_ThenCancellationPropagates()
    {
        // Given
        using var cancellation = new CancellationTokenSource();
        var heimdall = HeimdallStub.Hanging();
        var check = new HeimdallOperationalHealthCheck(
            new StubHttpClientFactory(heimdall), JwksTestSupport.Options(), TimeProvider.System);
        var running = check.CheckAsync(cancellation.Token);
        await heimdall.Started.Task.WaitAsync(Soon);

        // When
        await cancellation.CancelAsync();

        // Then
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Soon));
    }

    [UnitFact]
    public async Task GivenDatabaseDown_WhenInternalHealthRequested_ThenUnhealthy503()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs(
            StubCheck.Returning("Database", required: true, OperationalHealthStatus.Unhealthy),
            StubCheck.Returning("Heimdall", required: false, OperationalHealthStatus.Healthy)));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then the same body as ever, under a 503
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            """{"status":"Unhealthy","services":[{"name":"Database","status":"Unhealthy"},{"name":"Heimdall","status":"Healthy"}]}""",
            body);
    }

    [UnitFact]
    public async Task GivenInternalRoute_WhenRequested_ThenBodyMatchesTheContract()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs(
            StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy),
            StubCheck.Returning("Heimdall", required: false, OperationalHealthStatus.Unhealthy, "HTTP 503")));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then camelCase and bare, with no envelope around it and no reason where there is none
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"status":"Degraded","services":[{"name":"Database","status":"Healthy"},{"name":"Heimdall","status":"Unhealthy","reason":"HTTP 503"}]}""",
            body);
    }

    [UnitFact]
    public async Task GivenJobRunnerFigures_WhenInternalHealthRequested_ThenTheyStayInTheReport()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs(new StubCheck("JobRunner", required: true, _ =>
            Task.FromResult(new OperationalHealthCheckResult(
                "JobRunner", OperationalHealthStatus.Healthy, true, QueueDepth: 2, OldestPendingSeconds: 7)))));

        // When
        var body = await (await app.GetTestClient().GetAsync(Route)).Content.ReadAsStringAsync();

        // Then
        Assert.Equal(
            """{"status":"Healthy","services":[{"name":"JobRunner","status":"Healthy","queueDepth":2,"oldestPendingSeconds":7}]}""",
            body);
    }

    [UnitTheory]
    [InlineData(OperationalHealthStatus.Healthy, OperationalHealthStatus.Healthy, HttpStatusCode.OK, "Healthy")]
    [InlineData(OperationalHealthStatus.Healthy, OperationalHealthStatus.NotConfigured, HttpStatusCode.OK, "Healthy")]
    [InlineData(OperationalHealthStatus.Healthy, OperationalHealthStatus.Degraded, HttpStatusCode.OK, "Degraded")]
    [InlineData(OperationalHealthStatus.Healthy, OperationalHealthStatus.Unhealthy, HttpStatusCode.OK, "Degraded")]
    [InlineData(OperationalHealthStatus.Unhealthy, OperationalHealthStatus.Healthy, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData(OperationalHealthStatus.Degraded, OperationalHealthStatus.Healthy, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData(OperationalHealthStatus.NotConfigured, OperationalHealthStatus.Healthy, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task GivenCheckStatuses_WhenInternalHealthRequested_ThenAggregateFollowsTheEvaluatorsRule(
        OperationalHealthStatus required,
        OperationalHealthStatus optional,
        HttpStatusCode expectedCode,
        string expectedStatus)
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs(
            StubCheck.Returning("Required", required: true, required),
            StubCheck.Returning("Optional", required: false, optional)));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(expectedCode, response.StatusCode);
        Assert.StartsWith($$"""{"status":"{{expectedStatus}}",""", body);
    }

    [UnitFact]
    public async Task GivenApplicationPort_WhenInternalHealthRequested_Then404()
    {
        // Given
        var check = StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy);
        await using var app = await StartAsync(ApplicationPort, Stubs(check));

        // When
        var response = await app.GetTestClient().GetAsync(Route);

        // Then it is just another address the API does not have, and no check ran
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.Contains(ReachedTheApi));
        Assert.Equal(0, check.Runs);
    }

    [UnitTheory]
    [InlineData("/healthcheck")]
    [InlineData("/healthcheck/detailed")]
    [InlineData("/api/accounts")]
    [InlineData("/internal")]
    [InlineData("/internal/health/")]
    [InlineData("/internal/health/extra")]
    [InlineData("/")]
    public async Task GivenOtherPathOnMetricsPort_WhenRequested_Then404(string path)
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs());

        // When
        var response = await app.GetTestClient().GetAsync(path);

        // Then the port refused it, before anything of the API ran
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(ReachedTheApi));
    }

    [UnitTheory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task GivenAnotherMethodOnMetricsPort_WhenInternalHealthRequested_Then404(string method)
    {
        // Given
        var check = StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy);
        await using var app = await StartAsync(MetricsPort, Stubs(check));

        // When
        var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), Route));

        // Then
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(ReachedTheApi));
        Assert.Equal(0, check.Runs);
    }

    [UnitFact]
    public async Task GivenMetricsPort_WhenScraped_ThenTheScrapeIsStillServed()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs());

        // When
        var response = await app.GetTestClient().GetAsync(PrometheusMetrics.ScrapePath);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"service_name=\"{PrometheusMetrics.ServiceName}\"", body);
    }

    [UnitTheory]
    [InlineData(MetricsPort)]
    [InlineData(0)]
    public async Task GivenExporterDisabled_WhenInternalHealthRequested_ThenThereIsNoInternalRoute(int localPort)
    {
        // Given metrics off: port 0 means off, and 0 is also the local port an in-memory host reports
        var check = StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy);
        await using var app = await StartAsync(localPort, Stubs(check), metricsPort: 0);

        // When
        var response = await app.GetTestClient().GetAsync(Route);

        // Then
        Assert.True(response.Headers.Contains(ReachedTheApi));
        Assert.Equal(0, check.Runs);
    }

    [UnitFact]
    public async Task GivenAdminRoute_WhenRequested_ThenStillRequiresTheRoleAndListsHeimdall()
    {
        // Given the whole application, where Heimdall answers at first and later does not. Only the checks that
        // would need a database are replaced, so the Heimdall entry comes from the real registration.
        var previous = JwksTestSupport.Settings().Keys.ToDictionary(
            key => key, Environment.GetEnvironmentVariable);

        try
        {
            foreach (var (key, value) in JwksTestSupport.Settings())
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            var heimdall = new StubJwksHandler();
            heimdall.Serve(JwksTestSupport.KeySet(TestHeimdallKeySet.Key));
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<IUserProfileProvisioner>();
                    services.AddSingleton<IUserProfileProvisioner, StubUserProfileProvisioner>();
                    services.AddHttpClient(HeimdallJwks.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => heimdall);

                    foreach (var other in services
                                 .Where(service => service.ServiceType == typeof(IOperationalHealthCheck)
                                                   && service.ImplementationType !=
                                                   typeof(HeimdallOperationalHealthCheck))
                                 .ToList())
                    {
                        services.Remove(other);
                    }

                    services.AddScoped<IOperationalHealthCheck>(_ =>
                        StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy));
                });
            });
            using var anonymous = factory.CreateClient();
            using var user = factory.CreateClient();
            Authorize(user, HeimdallRoles.User);
            using var administrator = factory.CreateClient();
            Authorize(administrator, HeimdallRoles.SystemAdmin);

            // When
            var unauthorized = await anonymous.GetAsync("/healthcheck/detailed");
            var forbidden = await user.GetAsync("/healthcheck/detailed");
            var healthy = await administrator.GetAsync("/healthcheck/detailed");
            using var healthyReport = JsonDocument.Parse(await healthy.Content.ReadAsStringAsync());
            heimdall.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var degraded = await administrator.GetAsync("/healthcheck/detailed");
            using var degradedReport = JsonDocument.Parse(await degraded.Content.ReadAsStringAsync());

            // Then
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            var entry = Service(healthyReport, "Heimdall");
            Assert.Equal("Healthy", entry.GetProperty("status").GetString());
            Assert.False(entry.TryGetProperty("reason", out _));
            Assert.Equal(HttpStatusCode.OK, degraded.StatusCode);
            Assert.Equal("Degraded", degradedReport.RootElement.GetProperty("status").GetString());
            entry = Service(degradedReport, "Heimdall");
            Assert.Equal("Unhealthy", entry.GetProperty("status").GetString());
            Assert.Equal("HTTP 503", entry.GetProperty("reason").GetString());
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [UnitFact]
    public async Task GivenAReport_WhenRequestedWithinFiveSecondsOfFinishing_ThenTheChecksDoNotRunAgain()
    {
        // Given a check that takes two seconds of the clock, so finishing and starting are told apart
        var clock = new TimerClock();
        var check = new StubCheck("Database", required: true, _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(2));

            return Task.FromResult(Result("Database", required: true, OperationalHealthStatus.Healthy));
        });
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: clock);
        var client = app.GetTestClient();
        await client.GetAsync(Route);

        // When just inside five seconds of the end, which is past five from the start
        clock.Advance(CacheWindow - TimeSpan.FromMilliseconds(1));
        await client.GetAsync(Route);

        // Then
        Assert.Equal(1, check.Runs);
    }

    [UnitFact]
    public async Task GivenAReport_WhenRequestedFiveSecondsAfterFinishing_ThenTheChecksRunAgain()
    {
        // Given
        var clock = new TimerClock();
        var check = StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy);
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: clock);
        var client = app.GetTestClient();
        await client.GetAsync(Route);

        // When
        clock.Advance(CacheWindow);
        await client.GetAsync(Route);

        // Then
        Assert.Equal(2, check.Runs);
    }

    [UnitFact]
    public async Task GivenAFailedReport_WhenRequestedWithinTheWindow_ThenItIsCachedToo()
    {
        // Given
        var check = StubCheck.Throwing("Database", required: true, new InvalidOperationException(Secret));
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: new TimerClock());
        var client = app.GetTestClient();

        // When
        var first = await client.GetAsync(Route);
        var second = await client.GetAsync(Route);

        // Then
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(1, check.Runs);
    }

    [UnitFact]
    public async Task GivenChecksInFlight_WhenAnotherRequestArrives_ThenItSharesTheRun()
    {
        // Given a run held open
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = StubCheck.Gated("Database", required: true, gate);
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: new TimerClock());
        var client = app.GetTestClient();
        var first = client.GetAsync(Route);
        await check.Started.Task.WaitAsync(Soon);

        // When
        var second = client.GetAsync(Route);
        await Task.Delay(100);
        gate.SetResult();
        var responses = await Task.WhenAll(first, second).WaitAsync(Soon);

        // Then
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, check.Runs);
    }

    [UnitFact]
    public async Task GivenACallerGivesUp_WhenChecksAreInFlight_ThenTheRunGoesOnForTheOthers()
    {
        // Given a run started by a caller who then leaves
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = StubCheck.Gated("Database", required: true, gate);
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: new TimerClock());
        var client = app.GetTestClient();
        using var leaving = new CancellationTokenSource();
        var abandoned = client.GetAsync(Route, leaving.Token);
        await check.Started.Task.WaitAsync(Soon);
        await leaving.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(Soon));

        // When another caller waits on it
        var staying = client.GetAsync(Route);
        await Task.Delay(100);
        Assert.False(check.CancellationSeen);
        gate.SetResult();
        var response = await staying.WaitAsync(Soon);

        // Then the one run answered them, uncancelled
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, check.Runs);
    }

    [UnitFact]
    public async Task GivenChecksThatWaitForEachOther_WhenInternalHealthRequested_ThenTheyRunSideBySide()
    {
        // Given two checks that each finish only once the other has started: run one after another, neither
        // would finish before the bound. No clock moves here, so run one after another this test would hang.
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = StubCheck.Meeting("One", first, second);
        var other = StubCheck.Meeting("Other", second, first);
        await using var app = await StartAsync(MetricsPort, Stubs(one, other), clock: new TimerClock());

        // When
        var response = await app.GetTestClient().GetAsync(Route).WaitAsync(Soon);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(
            """{"status":"Healthy","services":[{"name":"One","status":"Healthy"},{"name":"Other","status":"Healthy"}]}""",
            body);
    }

    [UnitTheory]
    [InlineData(false, HttpStatusCode.OK, "Degraded")]
    [InlineData(true, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task GivenAHungCheckThatIgnoresCancellation_WhenTheBoundPasses_ThenTheReportSaysTimeout(
        bool required,
        HttpStatusCode expectedCode,
        string expectedStatus)
    {
        // Given a dependency that never answers and does not listen for cancellation, beside a healthy one
        var clock = new TimerClock();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = StubCheck.Returning("Fine", required: false, OperationalHealthStatus.Healthy);
        var hung = StubCheck.Gated("Hung", required, never);
        await using var app = await StartAsync(MetricsPort, Stubs(healthy, hung), clock: clock);
        var request = app.GetTestClient().GetAsync(Route);
        await hung.Started.Task.WaitAsync(Soon);

        // When
        clock.Advance(CheckBound);
        var response = await request.WaitAsync(Soon);
        var body = await response.Content.ReadAsStringAsync();
        never.SetResult();

        // Then
        Assert.Equal(expectedCode, response.StatusCode);
        Assert.Equal(
            $$"""{"status":"{{expectedStatus}}","services":[{"name":"Fine","status":"Healthy"},{"name":"Hung","status":"Unhealthy","reason":"timeout"}]}""",
            body);
    }

    [UnitFact]
    public async Task GivenACheckThatBlocksBeforeItsFirstAwait_WhenTheBoundPasses_ThenTheReportStillGoesOut()
    {
        // Given a check that holds its thread, so nothing but a bound outside it can end the wait
        var clock = new TimerClock();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new StubCheck("Blocking", required: false, _ =>
        {
            started.TrySetResult();
            release.Wait(Soon);

            return Task.FromResult(Result("Blocking", required: false, OperationalHealthStatus.Healthy));
        });
        await using var app = await StartAsync(MetricsPort, Stubs(blocking), clock: clock);
        var request = app.GetTestClient().GetAsync(Route);
        await started.Task.WaitAsync(Soon);

        // When
        clock.Advance(CheckBound);
        var response = await request.WaitAsync(Soon);
        var body = await response.Content.ReadAsStringAsync();
        release.Set();

        // Then
        Assert.Contains("\"reason\":\"timeout\"", body);
    }

    [UnitFact]
    public async Task GivenChecksThatShareAScopedDependency_WhenInternalHealthRequested_ThenEachGetsItsOwnScope()
    {
        // Given the shape of the real risk: Fortuna's checks are scoped and can share one DbContext
        var used = new ConcurrentBag<ScopedThing>();
        await using var app = await StartAsync(MetricsPort, services =>
        {
            services.RemoveAll<IOperationalHealthCheck>();
            services.AddScoped<ScopedThing>();

            foreach (var name in new[] { "One", "Two", "Three" })
            {
                services.AddScoped<IOperationalHealthCheck>(provider =>
                {
                    var thing = provider.GetRequiredService<ScopedThing>();

                    return new StubCheck(name, required: true, _ =>
                    {
                        used.Add(thing);

                        return Task.FromResult(Result(name, required: true, OperationalHealthStatus.Healthy));
                    });
                });
            }
        });

        // When
        var response = await app.GetTestClient().GetAsync(Route);

        // Then three checks, three instances, each disposed with its scope
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, used.Distinct().Count());
        await Task.WhenAll(used.Select(thing => thing.Disposed.Task)).WaitAsync(Soon);
    }

    [UnitFact]
    public async Task GivenAHungCheck_WhenItsBoundPasses_ThenItsScopeIsKeptUntilItSettles()
    {
        // Given a check still running after the report has gone out: disposing what it is using then could hang
        // or throw
        var clock = new TimerClock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<ScopedThing>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAsync(MetricsPort, services =>
        {
            services.RemoveAll<IOperationalHealthCheck>();
            services.AddScoped<ScopedThing>();
            services.AddScoped<IOperationalHealthCheck>(provider =>
            {
                var thing = provider.GetRequiredService<ScopedThing>();

                return new StubCheck("Hung", required: false, async _ =>
                {
                    started.TrySetResult(thing);
                    await release.Task;

                    return Result("Hung", required: false, OperationalHealthStatus.Healthy);
                });
            });
        }, clock: clock);
        var request = app.GetTestClient().GetAsync(Route);
        var thing = await started.Task.WaitAsync(Soon);
        clock.Advance(CheckBound);
        await request.WaitAsync(Soon);
        Assert.False(thing.Disposed.Task.IsCompleted);

        // When
        release.SetResult();

        // Then
        await thing.Disposed.Task.WaitAsync(Soon);
    }

    [UnitFact]
    public async Task GivenAThrowingCheck_WhenInternalHealthRequested_ThenTheReportCarriesOnlyAReasonWord()
    {
        // Given
        await using var app = await StartAsync(MetricsPort, Stubs(
            StubCheck.Throwing("Database", required: true, new InvalidOperationException(Secret)),
            StubCheck.Returning("Heimdall", required: false, OperationalHealthStatus.Healthy)));

        // When
        var response = await app.GetTestClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        // Then one failure does not cost the others their entry
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            """{"status":"Unhealthy","services":[{"name":"Database","status":"Unhealthy","reason":"request failed"},{"name":"Heimdall","status":"Healthy"}]}""",
            body);
    }

    [UnitFact]
    public async Task GivenACheckThatCannotBeBuilt_WhenInternalHealthRequested_Then503AndTheNextRunIsNotStuck()
    {
        // Given a registration that throws, with text the report must not repeat
        var clock = new TimerClock();
        var attempts = 0;
        await using var app = await StartAsync(MetricsPort, services =>
        {
            services.RemoveAll<IOperationalHealthCheck>();
            services.AddScoped<IOperationalHealthCheck>(_ =>
            {
                Interlocked.Increment(ref attempts);

                throw new InvalidOperationException(Secret);
            });
        }, clock: clock);
        var client = app.GetTestClient();

        // When
        var first = await client.GetAsync(Route).WaitAsync(Soon);
        var body = await first.Content.ReadAsStringAsync();
        clock.Advance(CacheWindow);
        var second = await client.GetAsync(Route).WaitAsync(Soon);

        // Then
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.DoesNotContain(Secret, body);
        Assert.Contains("\"reason\":\"request failed\"", body);
        Assert.Equal(2, attempts);
    }

    [UnitFact]
    public async Task GivenARunThatDiesAfterItsChecks_WhenTheNextRequestArrives_ThenTheSlotIsNotWedged()
    {
        // Given a logger that throws, the one thing outside the checks that a run still calls
        var clock = new TimerClock();
        var logs = new RecordingLogs { ThrowOnWrite = true };
        var check = StubCheck.Returning("Database", required: true, OperationalHealthStatus.Unhealthy);
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: clock, logs: logs);
        var client = app.GetTestClient();
        await Record.ExceptionAsync(() => client.GetAsync(Route).WaitAsync(Soon));
        logs.ThrowOnWrite = false;

        // When
        clock.Advance(CacheWindow);
        var response = await client.GetAsync(Route).WaitAsync(Soon);

        // Then a fresh run, not the first one's fault again
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, check.Runs);
    }

    [UnitFact]
    public async Task GivenACheckFailing_WhenRunRepeatedly_ThenItIsLoggedOncePerStatusChange()
    {
        // Given outcomes that stay failed, recover, then fail again
        var clock = new TimerClock();
        var logs = new RecordingLogs();
        var outcomes = new Queue<Func<OperationalHealthCheckResult>>([
            () => throw new HttpRequestException(Secret, new SocketException((int)SocketError.ConnectionRefused)),
            () => throw new HttpRequestException(Secret, new SocketException((int)SocketError.ConnectionRefused)),
            () => throw new TimeoutException(Secret),
            () => Result("Heimdall", required: false, OperationalHealthStatus.Healthy),
            () => Result("Heimdall", required: false, OperationalHealthStatus.Unhealthy, "HTTP 500")
        ]);
        var check = new StubCheck("Heimdall", required: false, _ => Task.FromResult(outcomes.Dequeue()()));
        await using var app = await StartAsync(MetricsPort, Stubs(check), clock: clock, logs: logs);
        var client = app.GetTestClient();

        // When
        for (var run = 0; run < 5; run++)
        {
            await client.GetAsync(Route);
            clock.Advance(CacheWindow);
        }

        // Then twice, with the reason word alone: no exception, so no stack trace
        Assert.Equal(5, check.Runs);
        Assert.Collection(
            logs.Entries,
            entry => Assert.Contains("connection refused", entry.Message),
            entry => Assert.Contains("HTTP 500", entry.Message));
        Assert.All(logs.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(Secret, entry.Message);
        });
    }

    [UnitFact]
    public async Task GivenHealthyAndNotConfiguredChecks_WhenRun_ThenNothingIsLogged()
    {
        // Given
        var logs = new RecordingLogs();
        await using var app = await StartAsync(MetricsPort, Stubs(
            StubCheck.Returning("Database", required: true, OperationalHealthStatus.Healthy),
            StubCheck.Returning("Heimdall", required: false, OperationalHealthStatus.NotConfigured)), logs: logs);

        // When
        await app.GetTestClient().GetAsync(Route);

        // Then
        Assert.Empty(logs.Entries);
    }

    [UnitFact]
    public void GivenAnUnknownException_WhenDescribed_ThenRequestFailedWithoutItsText() =>
        Assert.Equal("request failed", HealthReason.From(new InvalidOperationException(Secret)));

    [UnitFact]
    public void GivenCancellation_WhenDescribed_ThenTimeout() =>
        Assert.Equal("timeout", HealthReason.From(new TaskCanceledException(Secret)));

    [UnitFact]
    public void GivenATimeout_WhenDescribed_ThenTimeout() =>
        Assert.Equal("timeout", HealthReason.From(new TimeoutException(Secret)));

    [UnitTheory]
    [InlineData(SocketError.ConnectionRefused, "connection refused")]
    [InlineData(SocketError.HostNotFound, "name not resolved")]
    [InlineData(SocketError.TryAgain, "name not resolved")]
    [InlineData(SocketError.NoData, "name not resolved")]
    [InlineData(SocketError.HostUnreachable, "host unreachable")]
    [InlineData(SocketError.NetworkUnreachable, "host unreachable")]
    [InlineData(SocketError.ConnectionReset, "connection reset")]
    [InlineData(SocketError.TimedOut, "timeout")]
    [InlineData(SocketError.AccessDenied, "connection failed")]
    public void GivenASocketError_WhenDescribed_ThenItsWord(SocketError error, string expected) =>
        Assert.Equal(expected, HealthReason.From(new HttpRequestException(Secret, new SocketException((int)error))));

    [UnitTheory]
    [InlineData(HttpRequestError.NameResolutionError, "name not resolved")]
    [InlineData(HttpRequestError.ConnectionError, "connection failed")]
    [InlineData(HttpRequestError.SecureConnectionError, "tls error")]
    [InlineData(HttpRequestError.ResponseEnded, "connection closed")]
    [InlineData(HttpRequestError.InvalidResponse, "invalid response")]
    [InlineData(HttpRequestError.Unknown, "request failed")]
    public void GivenARequestError_WhenDescribed_ThenItsWord(HttpRequestError error, string expected) =>
        Assert.Equal(expected, HealthReason.From(new HttpRequestException(error, Secret)));

    [UnitFact]
    public void GivenADatabaseDriverFailure_WhenDescribed_ThenTheCauseInsideItIsNamed()
    {
        // Given the driver and EF Core each wrapping the socket's error in an exception of their own
        var refused = new Exception(Secret, new SocketException((int)SocketError.ConnectionRefused));
        var timedOut = new Exception(Secret, new TimeoutException(Secret));
        var tls = new Exception(Secret, new AuthenticationException(Secret));

        // When
        var reasons = new[] { refused, timedOut, tls }
            .Select(cause => HealthReason.From(new InvalidOperationException(Secret, cause)));

        // Then
        Assert.Equal(["connection refused", "timeout", "tls error"], reasons);
    }

    private static async Task<WebApplication> StartAsync(
        int localPort,
        Action<IServiceCollection> configure,
        int metricsPort = MetricsPort,
        TimerClock? clock = null,
        RecordingLogs? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddPrometheusMetrics(metricsPort);
        configure(builder.Services);

        if (clock is not null)
        {
            builder.Services.RemoveAll<TimeProvider>();
            builder.Services.AddSingleton<TimeProvider>(clock);
        }

        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.LocalPort = localPort;

            return next(context);
        });
        app.UsePrometheusMetrics(metricsPort);
        app.Run(context =>
        {
            context.Response.Headers[ReachedTheApi] = "true";
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        });
        await app.StartAsync();

        return app;
    }

    // The real check, with only its dependencies replaced.
    private static Action<IServiceCollection> RealHeimdall(HeimdallStub heimdall, FortunaOptions? options = null) =>
        services =>
        {
            services.AddSingleton(options ?? JwksTestSupport.Options());
            services.AddScoped<IOperationalHealthCheck, HeimdallOperationalHealthCheck>();
            services.AddHttpClient(HeimdallJwks.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => heimdall);
        };

    private static Action<IServiceCollection> Stubs(params StubCheck[] checks) =>
        services =>
        {
            foreach (var check in checks)
            {
                services.AddScoped<IOperationalHealthCheck>(_ => check);
            }
        };

    private static OperationalHealthCheckResult Result(
        string name,
        bool required,
        OperationalHealthStatus status,
        string? reason = null) => new(name, status, required, Reason: reason);

    private static JsonElement Service(JsonDocument report, string name) =>
        report.RootElement.GetProperty("services").EnumerateArray()
            .Single(service => service.GetProperty("name").GetString() == name);

    private static void Authorize(HttpClient client, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Health Caller"
        };
        var configuration = new JwtConfiguration(
            3600,
            JwksTestSupport.Issuer,
            JwksTestSupport.Audience,
            string.Empty,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestHeimdallKeySet.Sign(configuration));
    }

    private sealed class StubCheck(
        string name,
        bool required,
        Func<CancellationToken, Task<OperationalHealthCheckResult>> run) : IOperationalHealthCheck
    {
        private int _runs;
        private CancellationToken _token;

        public int Runs => Volatile.Read(ref _runs);
        public bool CancellationSeen => _token.IsCancellationRequested;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => name;
        public bool IsRequired => required;

        public static StubCheck Returning(
            string name,
            bool required,
            OperationalHealthStatus status,
            string? reason = null) =>
            new(name, required, _ => Task.FromResult(Result(name, required, status, reason)));

        public static StubCheck Throwing(string name, bool required, Exception exception) =>
            new(name, required, _ => Task.FromException<OperationalHealthCheckResult>(exception));

        // Deliberately does not watch the token: a dependency that hangs is not obliged to cooperate.
        public static StubCheck Gated(string name, bool required, TaskCompletionSource gate) =>
            new(name, required, async _ =>
            {
                await gate.Task;

                return Result(name, required, OperationalHealthStatus.Healthy);
            });

        // Says it has started, then waits until the other one has too.
        public static StubCheck Meeting(string name, TaskCompletionSource mine, TaskCompletionSource theirs) =>
            new(name, false, async _ =>
            {
                mine.TrySetResult();
                await theirs.Task;

                return Result(name, false, OperationalHealthStatus.Healthy);
            });

        public Task<OperationalHealthCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            _token = cancellationToken;
            Interlocked.Increment(ref _runs);
            Started.TrySetResult();

            return run(cancellationToken);
        }
    }

    // A scoped service with the lifetime of the scope that made it, as an AppDbContext has.
    private sealed class ScopedThing : IDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Disposed.TrySetResult();
    }

    // Stands in for Heimdall behind the check's own HttpClient, and keeps what the check sent.
    private sealed class HeimdallStub : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _answer;

        private HeimdallStub(Func<CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

        public ConcurrentQueue<Captured> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static HeimdallStub Answering(HttpStatusCode status = HttpStatusCode.OK) =>
            new(_ => Task.FromResult(new HttpResponseMessage(status)));

        public static HeimdallStub Refusing() =>
            new(_ => throw new HttpRequestException(Secret, new SocketException((int)SocketError.ConnectionRefused)));

        public static HeimdallStub Hanging() =>
            new(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(new Captured(
                request.Method,
                request.RequestUri?.ToString(),
                request.Headers.Authorization,
                request.Content is not null));
            Started.TrySetResult();

            return _answer(cancellationToken);
        }
    }

    private sealed record Captured(
        HttpMethod Method,
        string? Uri,
        AuthenticationHeaderValue? Authorization,
        bool HasBody);

    private sealed class StubUserProfileProvisioner : IUserProfileProvisioner
    {
        public Task<UserProfileSnapshot> GetOrCreateAsync(
            Guid externalSubject,
            string displayName,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;

            return Task.FromResult(new UserProfileSnapshot(
                Guid.NewGuid(), externalSubject, displayName, "BRL", false, now, now));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    // Keeps only what the report's own class writes: the host logs too, and those lines are not under test.
    private sealed class RecordingLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;
        public bool ThrowOnWrite { get; set; }

        public ILogger CreateLogger(string categoryName) =>
            categoryName.Contains(nameof(InternalHealthEndpoint), StringComparison.Ordinal)
                ? new Recorder(this)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Recorder(RecordingLogs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logs.ThrowOnWrite)
                {
                    throw new InvalidOperationException(Secret);
                }

                logs._entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }

    // A clock whose timers fire when a test moves it, so a three-second bound costs no three seconds.
    private sealed class TimerClock : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ManualTimer(this, () => callback(state), _now + dueTime);
                _timers.Add(timer);

                return timer;
            }
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;

            lock (_gate)
            {
                _now += by;
                due = _timers.Where(timer => timer.DueAt <= _now).ToList();
                _timers.RemoveAll(due.Contains);
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(TimerClock clock, Action fire, DateTimeOffset dueAt) : ITimer
        {
            public DateTimeOffset DueAt { get; } = dueAt;

            public void Fire() => fire();

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => clock.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}
