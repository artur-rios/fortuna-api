using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     The whole path to Heimdall and back, with only the network replaced: the app's own pipeline,
///     forwarded-headers handling, typed client, caller headers handler and gateway, against a fake
///     Heimdall that answers as the real one does.
/// </summary>
public sealed class HeimdallRelayEndToEndTests : IDisposable
{
    private const string CallerKey = "caller-key-with-at-least-32-characters-0123";
    private const string Proxy = "10.1.2.3";
    private const string EndUser = "203.0.113.50";

    private static readonly string[] OverriddenSettings =
    [
        "FORTUNA_HEIMDALL_CALLER_KEY",
        "FORTUNA_FORWARDED_KNOWN_NETWORKS",
        "FORTUNA_FORWARDED_KNOWN_PROXIES"
    ];

    [FunctionalFact]
    public async Task GivenACallerKeyBehindATrustedProxy_WhenHeimdallRateLimits_ThenTheUserIsNamedAndTheWaitRelayed()
    {
        // Given — Heimdall's limiter refuses, as it answers: its envelope and a Retry-After.
        var heimdall = new FakeHeimdall(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    "{\"data\":null,\"messages\":[],\"errors\":[\"Too many requests.\"],\"success\":false}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation("Retry-After", "42");

            return response;
        });
        await using var factory = CreateFactory(heimdall, CallerKey);
        using var client = factory.CreateClient();

        // When — the request reaches Fortuna through its reverse proxy, which names the end user.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "user@example.test", password = "secret-in-transit" })
        };
        request.Headers.Add("X-Forwarded-For", EndUser);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Then — Heimdall was told whose request it was, by the address forwarded-headers resolved…
        Assert.Equal([CallerKey], heimdall.Values(HeimdallCallerHeadersHandler.CallerKeyHeader));
        Assert.Equal([EndUser], heimdall.Values(HeimdallCallerHeadersHandler.ClientAddressHeader));
        // …and the client got Heimdall's refusal as Heimdall gave it.
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(42), response.Headers.RetryAfter?.Delta);
        Assert.Contains("\"errors\":[\"Too many requests.\"]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-in-transit", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenNoCallerKey_WhenHeimdallAnswersConflict_ThenNoCallerHeadersAreSentAndConflictIsRelayed()
    {
        // Given
        var heimdall = new FakeHeimdall(() => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                "{\"data\":null,\"messages\":[],\"errors\":[\"A conflicting account exists.\"],\"success\":false}",
                Encoding.UTF8,
                "application/json")
        });
        await using var factory = CreateFactory(heimdall, callerKey: null);
        using var client = factory.CreateClient();

        // When
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google")
        {
            Content = JsonContent.Create(new { idToken = "id-token" })
        };
        request.Headers.Add("X-Forwarded-For", EndUser);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(1, heimdall.Calls);
        Assert.Empty(heimdall.Values(HeimdallCallerHeadersHandler.CallerKeyHeader));
        Assert.Empty(heimdall.Values(HeimdallCallerHeadersHandler.ClientAddressHeader));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Contains("A conflicting account exists.", body, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var setting in OverriddenSettings)
        {
            Environment.SetEnvironmentVariable(setting, null);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeHeimdall heimdall, string? callerKey)
    {
        foreach (var setting in ValidSettings(callerKey))
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IStartupFilter>(new ConnectionStartupFilter(IPAddress.Parse(Proxy)));
                // Replaces only the network under the typed client the app registered.
                services.AddHttpClient<IHeimdallAuthGateway, HeimdallAuthGateway>()
                    .ConfigurePrimaryHttpMessageHandler(() => heimdall);
            });
        });
    }

    private static Dictionary<string, string?> ValidSettings(string? callerKey) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_HEIMDALL_CALLER_KEY"] = callerKey,
        ["FORTUNA_FORWARDED_KNOWN_NETWORKS"] = "10.0.0.0/8",
        ["FORTUNA_FORWARDED_KNOWN_PROXIES"] = null
    };

    /// <summary>Stands in for Heimdall's network: records what arrived and answers as configured.</summary>
    private sealed class FakeHeimdall(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        // Copied on arrival: the gateway disposes each request once it has its answer.
        private readonly List<(string Name, string Value)> _headers = [];

        public int Calls { get; private set; }

        public IReadOnlyList<string> Values(string header) =>
            _headers.Where(entry => entry.Name == header).Select(entry => entry.Value).ToList();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            _headers.AddRange(request.Headers.SelectMany(
                header => header.Value.Select(value => (header.Key, value))));

            return Task.FromResult(answer());
        }
    }

    /// <summary>Gives every request the reverse proxy's connection address.</summary>
    private sealed class ConnectionStartupFilter(IPAddress remoteAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteAddress;

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
