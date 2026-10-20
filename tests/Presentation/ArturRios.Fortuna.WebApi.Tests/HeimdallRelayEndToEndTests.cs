using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
    private const string DefaultHeimdallBaseUrl = "https://heimdall.example.test/";

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

    [FunctionalFact]
    public async Task GivenPlainHttpHeimdall_WhenItRedirects_ThenTheRedirectIsNotFollowed()
    {
        // Given — Heimdall on plain HTTP redirects to a second server, which would sign the user in.
        const string signedIn =
            "{\"data\":{\"token\":\"issued\",\"expiresAt\":\"2026-09-09T13:00:00Z\",\"emailVerified\":true,\"requiresTwoFactor\":false}}";
        using var redirected = new LoopbackHttpServer(_ =>
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(signedIn)}\r\nConnection: close\r\n\r\n{signedIn}");
        using var heimdall = new LoopbackHttpServer(_ =>
            "HTTP/1.1 307 Temporary Redirect\r\n" +
            $"Location: http://127.0.0.1:{redirected.Port}/api/auth/login\r\n" +
            "Content-Length: 0\r\nConnection: close\r\n\r\n");
        await using var factory = CreateFactory(
            heimdall: null,
            CallerKey,
            heimdallBaseUrl: $"http://127.0.0.1:{heimdall.Port}/");
        using var client = factory.CreateClient();

        // When
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "user@example.test", password = "secret-in-transit" });

        // Then — Heimdall was reached over the app's own transport, and its redirect went nowhere.
        Assert.Equal(1, heimdall.Connections);
        Assert.False(response.IsSuccessStatusCode, $"Fortuna answered {(int)response.StatusCode}.");
        Assert.Equal(0, redirected.Connections);
    }

    [FunctionalFact]
    public async Task GivenClientForwardedFor_WhenRelayedToHeimdall_ThenNoForwardingHeaderReachesHeimdall()
    {
        // Given
        var heimdall = new FakeHeimdall(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                "{\"data\":null,\"messages\":[],\"errors\":[\"Invalid credentials.\"],\"success\":false}",
                Encoding.UTF8,
                "application/json")
        });
        await using var factory = CreateFactory(heimdall, CallerKey);
        using var client = factory.CreateClient();

        // When — the client names an address of its own choosing, in both forwarding headers.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "user@example.test", password = "secret-in-transit" })
        };
        request.Headers.Add("X-Forwarded-For", "198.51.100.7");
        request.Headers.Add("Forwarded", "for=198.51.100.7");
        using var response = await client.SendAsync(request);

        // Then — Heimdall hears of the user only through the caller headers, never a forwarding header.
        Assert.Equal(1, heimdall.Calls);
        Assert.Empty(heimdall.Values("X-Forwarded-For"));
        Assert.Empty(heimdall.Values("Forwarded"));
        Assert.Empty(heimdall.Values("X-Real-IP"));
    }

    [Ipv6LoopbackFact]
    public async Task GivenPlainHttpHeimdallOnIpv6Loopback_WhenTheAppsClientSends_ThenTheServerAnswers()
    {
        // Given — Heimdall on [::1]: the app's handler hands the connector the bracketed literal.
        using var heimdall = new LoopbackHttpServer(
            _ => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            IPAddress.IPv6Loopback);
        await using var factory = CreateFactory(
            heimdall: null,
            CallerKey,
            heimdallBaseUrl: $"http://[::1]:{heimdall.Port}/");
        var client = factory.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IHeimdallAuthGateway));

        // When — the typed client the app registered, with its own primary handler and connector.
        using var response = await client.GetAsync(client.BaseAddress);

        // Then
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, heimdall.Connections);
    }

    public void Dispose()
    {
        foreach (var setting in OverriddenSettings)
        {
            Environment.SetEnvironmentVariable(setting, null);
        }

        // The test run's settings name this default; other classes read it from the environment.
        Environment.SetEnvironmentVariable("FORTUNA_HEIMDALL_BASE_URL", DefaultHeimdallBaseUrl);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeHeimdall? heimdall,
        string? callerKey,
        string heimdallBaseUrl = DefaultHeimdallBaseUrl)
    {
        foreach (var setting in ValidSettings(callerKey, heimdallBaseUrl))
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
                if (heimdall is not null)
                {
                    // Replaces only the network under the typed client the app registered.
                    services.AddHttpClient<IHeimdallAuthGateway, HeimdallAuthGateway>()
                        .ConfigurePrimaryHttpMessageHandler(() => heimdall);
                }
            });
        });
    }

    private static Dictionary<string, string?> ValidSettings(string? callerKey, string heimdallBaseUrl) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = TestDatabase.Unreachable,
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
        ["FORTUNA_HEIMDALL_BASE_URL"] = heimdallBaseUrl,
        ["FORTUNA_HEIMDALL_JWKS_URL"] = "http://heimdall.example.test/.well-known/jwks.json",
        ["FORTUNA_LOCAL_TOKEN_SECRET"] = "fortuna-tests-local-token-secret-with-enough-entropy",
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

    /// <summary>
    ///     A server on a loopback port that reads each request's head, answers it with a fixed raw
    ///     response and closes the connection, counting the connections it accepted.
    /// </summary>
    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private int _connections;

        public LoopbackHttpServer(Func<string, string> answer, IPAddress? address = null)
        {
            _listener = new TcpListener(address ?? IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = ServeAsync(answer);
        }

        public int Port { get; }

        public int Connections => Volatile.Read(ref _connections);

        public void Dispose()
        {
            // The source is cancelled, not disposed: the serving loop may still be reading its token.
            _stopping.Cancel();
            _listener.Stop();
        }

        private async Task ServeAsync(Func<string, string> answer)
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or
                                                      ObjectDisposedException or SocketException)
                {
                    // The test is over: the listener stopped.
                    return;
                }

                Interlocked.Increment(ref _connections);
                using (connection)
                {
                    try
                    {
                        var stream = connection.GetStream();
                        var head = await ReadHeadAsync(stream, _stopping.Token);
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(answer(head)), _stopping.Token);
                        // Ends the answer cleanly, then drains what the client still sends, so closing
                        // never resets the connection under a response the client has yet to read.
                        connection.Client.Shutdown(SocketShutdown.Send);
                        await DrainAsync(stream, _stopping.Token);
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or
                                                          IOException or SocketException)
                    {
                        // The client went away, or the test is over; the next connection is served.
                    }
                }
            }
        }

        private static async Task DrainAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[4096];
            while (await stream.ReadAsync(buffer, timeout.Token) > 0)
            {
            }
        }

        private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            return received.ToString();
        }
    }

    /// <summary>A functional fact, skipped with its reason where the host has no IPv6 loopback.</summary>
    private sealed class Ipv6LoopbackFactAttribute : FunctionalFactAttribute
    {
        public Ipv6LoopbackFactAttribute()
        {
            if (Skip is null && !HasIpv6Loopback())
            {
                Skip = "This host has no IPv6 loopback address (::1) to listen on.";
            }
        }

        private static bool HasIpv6Loopback()
        {
            if (!Socket.OSSupportsIPv6)
            {
                return false;
            }

            try
            {
                var probe = new TcpListener(IPAddress.IPv6Loopback, 0);
                probe.Start();
                probe.Stop();

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
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
