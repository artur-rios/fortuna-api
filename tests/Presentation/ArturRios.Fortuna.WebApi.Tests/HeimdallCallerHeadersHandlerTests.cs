using System.Net;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Extensions;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class HeimdallCallerHeadersHandlerTests
{
    private const string CallerKey = "caller-key-with-at-least-32-characters-0123";
    private const string KeyHeader = "X-Heimdall-Caller-Key";
    private const string AddressHeader = "X-Heimdall-Client-Address";

    [UnitFact]
    public async Task GivenKeyAndClientAddress_WhenSending_ThenBothHeadersAreSet()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, Accessor(IPAddress.Parse("203.0.113.9")), recorder);

        Assert.Equal([CallerKey], recorder.Values(KeyHeader));
        Assert.Equal(["203.0.113.9"], recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenExistingHeaders_WhenSending_ThenTheyAreOverwritten()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, Accessor(IPAddress.Parse("203.0.113.9")), recorder, request =>
        {
            request.Headers.Add(KeyHeader, "other-key");
            request.Headers.Add(AddressHeader, "198.51.100.1");
        });

        Assert.Equal([CallerKey], recorder.Values(KeyHeader));
        Assert.Equal(["203.0.113.9"], recorder.Values(AddressHeader));
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GivenNoKey_WhenSending_ThenNeitherHeaderIsSent(string? key)
    {
        var recorder = new RecordingHandler();

        await Send(key, Accessor(IPAddress.Parse("203.0.113.9")), recorder);

        Assert.Empty(recorder.Values(KeyHeader));
        Assert.Empty(recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenNoKeyAndExistingHeaders_WhenSending_ThenNeitherHeaderIsSent()
    {
        var recorder = new RecordingHandler();

        await Send(null, Accessor(IPAddress.Parse("203.0.113.9")), recorder, request =>
        {
            request.Headers.Add(KeyHeader, "other-key");
            request.Headers.Add(AddressHeader, "198.51.100.1");
        });

        Assert.Empty(recorder.Values(KeyHeader));
        Assert.Empty(recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenNoHttpContext_WhenSending_ThenNeitherHeaderIsSent()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, new HttpContextAccessor(), recorder);

        Assert.Empty(recorder.Values(KeyHeader));
        Assert.Empty(recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenNullRemoteAddress_WhenSending_ThenNeitherHeaderIsSent()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, Accessor(null), recorder);

        Assert.Empty(recorder.Values(KeyHeader));
        Assert.Empty(recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenIpv4MappedAddress_WhenSending_ThenItIsSentAsIpv4()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, Accessor(IPAddress.Parse("::ffff:203.0.113.9")), recorder);

        Assert.Equal(["203.0.113.9"], recorder.Values(AddressHeader));
    }

    [UnitFact]
    public async Task GivenIpv6Address_WhenSending_ThenTheFullAddressIsSent()
    {
        var recorder = new RecordingHandler();

        await Send(CallerKey, Accessor(IPAddress.Parse("2001:db8::1")), recorder);

        Assert.Equal(["2001:db8::1"], recorder.Values(AddressHeader));
    }

    [FunctionalFact]
    public async Task GivenTheTypedClientBuiltAsTheAppDoes_WhenCallingHeimdall_ThenHeadersArrive()
    {
        var primary = new RecordingHandler();
        await using var provider = BuildProvider(CallerKey, Accessor(IPAddress.Parse("203.0.113.9")), primary);

        await provider.GetRequiredService<IHeimdallAuthGateway>()
            .LoginAsync("user@example.test", "credential", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal([CallerKey], primary.Values(KeyHeader));
        Assert.Equal(["203.0.113.9"], primary.Values(AddressHeader));
        Assert.Equal("heimdall.example.test", primary.Host);
    }

    [FunctionalFact]
    public async Task GivenNoKeyConfigured_WhenCallingHeimdallThroughTheTypedClient_ThenNoHeadersArrive()
    {
        var primary = new RecordingHandler();
        await using var provider = BuildProvider(null, Accessor(IPAddress.Parse("203.0.113.9")), primary);

        await provider.GetRequiredService<IHeimdallAuthGateway>()
            .LoginAsync("user@example.test", "credential", Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(primary.Values(KeyHeader));
        Assert.Empty(primary.Values(AddressHeader));
    }

    private static ServiceProvider BuildProvider(
        string? callerKey,
        IHttpContextAccessor accessor,
        RecordingHandler primary)
    {
        var options = FortunaOptions.From(Settings(callerKey).GetValueOrDefault);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(accessor);
        services.AddFortunaIntegrations(options);
        // Adds only the fake transport to the typed client AddFortunaIntegrations registered.
        services.AddHttpClient<IHeimdallAuthGateway, HeimdallAuthGateway>()
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        return services.BuildServiceProvider();
    }

    private static async Task Send(
        string? key,
        IHttpContextAccessor accessor,
        RecordingHandler recorder,
        Action<HttpRequestMessage>? prepare = null)
    {
        using var invoker = new HttpMessageInvoker(new HeimdallCallerHeadersHandler(accessor, key)
        {
            InnerHandler = recorder
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://heimdall.example.test/api/auth/login");
        prepare?.Invoke(request);

        using var response = await invoker.SendAsync(request, CancellationToken.None);
    }

    private static HttpContextAccessor Accessor(IPAddress? remote)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote;

        return new HttpContextAccessor { HttpContext = context };
    }

    private static Dictionary<string, string?> Settings(string? callerKey) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_HEIMDALL_CALLER_KEY"] = callerKey
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);

        public string? Host { get; private set; }

        public IReadOnlyList<string> Values(string name) =>
            headers.TryGetValue(name, out var values) ? values : [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Host = request.RequestUri?.Host;
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
