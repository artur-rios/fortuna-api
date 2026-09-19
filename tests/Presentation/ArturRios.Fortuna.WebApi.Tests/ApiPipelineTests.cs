using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// Pipeline behavior that needs no database: request-body binding errors, unhandled exceptions
/// and client identification behind a reverse proxy.
/// </summary>
public sealed class ApiPipelineTests
{
    private const string AuthenticatePath = "/api/local-accounts/authenticate";

    [FunctionalFact]
    public async Task GivenMalformedBody_WhenBound_ThenBadRequestUsesTheDataOutputEnvelope()
    {
        await using var factory = CreateFactory(Environments.Development);
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"name\": 5, \"secret\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(AuthenticatePath, content);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.NotEmpty(envelope.Errors);
        Assert.Null(envelope.Data);
    }

    [FunctionalFact]
    public async Task GivenUnhandledException_WhenOutsideDevelopment_ThenGenericDataOutput500IsReturned()
    {
        await using var factory = CreateFactory(Environments.Production, localAuthenticationEnabled: true, services =>
        {
            services.RemoveAll<ILocalAccountStore>();
            services.AddScoped<ILocalAccountStore, ThrowingLocalAccountStore>();
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(AuthenticatePath, new { Name = "Local User", Secret = "secret-value" });
        var body = await response.Content.ReadAsStringAsync();
        var envelope = await response.Content.ReadFromJsonAsync<Envelope>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal([RequestMessages.UnexpectedError], envelope!.Errors);
        Assert.False(envelope.Success);
        Assert.DoesNotContain(ThrowingLocalAccountStore.Detail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenTrustedProxy_WhenClientsExhaustTheirLimit_ThenOtherForwardedClientsAreUnaffected()
    {
        await using var factory = CreateFactory(
            Environments.Development,
            configureServices: services => services.AddSingleton<IStartupFilter>(
                new RemoteAddressStartupFilter(IPAddress.Parse("10.1.2.3"))),
            forwardedNetworks: "10.0.0.0/8");
        using var client = factory.CreateClient();

        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            limited?.Dispose();
            limited = await SendAsFromAsync(client, "203.0.113.7");
        }

        using var otherClient = await SendAsFromAsync(client, "203.0.113.8");

        using (limited)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited!.StatusCode);
        }

        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherClient.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenUntrustedProxy_WhenForwardedHeaderIsSpoofed_ThenTheConnectionAddressIsLimited()
    {
        await using var factory = CreateFactory(
            Environments.Development,
            configureServices: services => services.AddSingleton<IStartupFilter>(
                new RemoteAddressStartupFilter(IPAddress.Parse("198.51.100.20"))),
            forwardedNetworks: "10.0.0.0/8");
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var _ = await SendAsFromAsync(client, $"203.0.113.{attempt + 1}");
        }

        using var response = await SendAsFromAsync(client, "203.0.113.99");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsFromAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthenticatePath)
        {
            Content = JsonContent.Create(new { Name = "Local User", Secret = "secret-value" })
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool localAuthenticationEnabled = false,
        Action<IServiceCollection>? configureServices = null,
        string? forwardedNetworks = null)
    {
        foreach (var setting in ValidSettings(localAuthenticationEnabled, forwardedNetworks))
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                configureServices?.Invoke(services);
            });
        });
    }

    private static Dictionary<string, string?> ValidSettings(bool localAuthenticationEnabled, string? forwardedNetworks) => new()
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
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = localAuthenticationEnabled.ToString(),
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = null,
        ["FORTUNA_RATES_SYNC_CRON"] = null,
        ["FORTUNA_RATES_CURRENCIES"] = null,
        ["FORTUNA_FORWARDED_KNOWN_NETWORKS"] = forwardedNetworks
    };

    private sealed record Envelope(object? Data, IReadOnlyCollection<string> Errors, bool Success);

    /// <summary>The test server has no socket, so the connection address is set explicitly.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class ThrowingLocalAccountStore : ILocalAccountStore
    {
        public const string Detail = "credential store exploded with internal detail";

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Detail);

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
            string name,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Detail);

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Detail);

        public Task<LocalAccountCreationResult> CreateAsync(
            LocalAccountCreation creation,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Detail);

        public Task<LocalAccountRecoveryResult> RecoverAsync(
            LocalAccountRecovery recovery,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Detail);

        public Task<bool> RegenerateRecoveryCodesAsync(
            LocalAccountRecoveryCodeRegeneration regeneration,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Detail);
    }
}
