using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class DataSourceDiscoveryTests
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";

    [FunctionalFact]
    public async Task GivenDefaultDeployment_WhenSourcesRequested_ThenFilesWorkAndPluggyExplainsConfig()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, HeimdallRoles.User);

        var response = await client.GetAsync("/api/data-sources");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var sources = (await response.Content.ReadFromJsonAsync<DataSourceEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, sources.Sources.Count);
        var excel = Assert.Single(sources.Sources, item => item.Name == "excel");
        Assert.Equal(DataSourceKind.File, excel.Kind);
        Assert.True(excel.IsAvailable);
        Assert.Contains(".xlsx", excel.SupportedFormats);
        Assert.Contains("Caller-mapped worksheet", excel.SupportedLayouts);
        var pdf = Assert.Single(sources.Sources, item => item.Name == "nubank-pdf");
        Assert.True(pdf.IsAvailable);
        Assert.Contains(".pdf", pdf.SupportedFormats);
        Assert.Contains("Nubank credit card invoice", pdf.SupportedLayouts);
        var pluggy = Assert.Single(sources.Sources, item => item.Name == "pluggy");
        Assert.Equal(DataSourceKind.Network, pluggy.Kind);
        Assert.False(pluggy.IsAvailable);
        Assert.Equal(DataSourceMessages.ConfigurationRequired, pluggy.UnavailableReason);
    }

    [FunctionalFact]
    public async Task GivenOfflineDeployment_WhenSourcesRequested_ThenOnlyNetworkSourceIsUnavailable()
    {
        await using var factory = CreateFactory(localAuthEnabled: true, configurePluggy: true);
        using var client = factory.CreateClient();
        Authorize(client, HeimdallRoles.User);

        var sources = (await client.GetFromJsonAsync<DataSourceEnvelope>(
            "/api/data-sources"))!.Data!;

        Assert.All(sources.Sources.Where(item => item.Kind == DataSourceKind.File),
            item => Assert.True(item.IsAvailable));
        var pluggy = Assert.Single(sources.Sources, item => item.Name == "pluggy");
        Assert.False(pluggy.IsAvailable);
        Assert.Equal(DataSourceMessages.NetworkUnavailable, pluggy.UnavailableReason);
    }

    [FunctionalFact]
    public async Task GivenAdditionalImplementation_WhenSourcesRequested_ThenItAppearsAutomatically()
    {
        await using var factory = CreateFactory(addCustomSource: true);
        using var client = factory.CreateClient();
        Authorize(client, HeimdallRoles.User);

        var sources = (await client.GetFromJsonAsync<DataSourceEnvelope>(
            "/api/data-sources"))!.Data!;

        var custom = Assert.Single(sources.Sources, item => item.Name == "custom-csv");
        Assert.Equal("Custom CSV", custom.DisplayName);
        Assert.Contains(".csv", custom.SupportedFormats);
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenSourcesRequested_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, HeimdallRoles.SystemAdmin);

        var unauthorized = await anonymous.GetAsync("/api/data-sources");
        var forbidden = await administrator.GetAsync("/api/data-sources");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        bool localAuthEnabled = false,
        bool configurePluggy = false,
        bool addCustomSource = false)
    {
        foreach (var setting in ValidSettings(localAuthEnabled, configurePluggy))
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                if (addCustomSource)
                {
                    services.AddSingleton<IIngestionSource, CustomSource>();
                }
            });
        });
    }

    private static void Authorize(HttpClient client, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner",
            IsLocal = true
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings(
        bool localAuthEnabled,
        bool configurePluggy) => new()
        {
            ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
            ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
            ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
            ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
            ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
            ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
            ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
            ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
            ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
            ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
            ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
            ["FORTUNA_LOCALE"] = "pt-BR",
            ["FORTUNA_LOCAL_AUTH_ENABLED"] = localAuthEnabled.ToString(),
            ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
            ["FORTUNA_PLUGGY_CLIENT_ID"] = configurePluggy ? "client" : null,
            ["FORTUNA_PLUGGY_CLIENT_SECRET"] = configurePluggy ? "secret" : null,
            ["FORTUNA_PLUGGY_BASE_URL"] = configurePluggy ? "https://pluggy.example" : null
        };

    private sealed record DataSourceEnvelope(DataSourceListData? Data);
    private sealed record DataSourceListData(IReadOnlyList<DataSourceData> Sources);
    private sealed record DataSourceData(
        string Name,
        DataSourceKind Kind,
        string DisplayName,
        bool IsNetworkBacked,
        bool IsAvailable,
        string? UnavailableReason,
        IReadOnlyList<string> RequiredInputs,
        IReadOnlyList<string> SupportedFormats,
        IReadOnlyList<string> SupportedLayouts);

    private sealed class CustomSource : IIngestionSource
    {
        public string Name => "custom-csv";

        public DataSourceSnapshot Describe() => new(
            Name,
            DataSourceKind.File,
            "Custom CSV",
            false,
            true,
            null,
            ["CSV file"],
            [".csv"],
            ["Custom layout"]);
    }
}
