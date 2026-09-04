using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class CurrencyEndpointsTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenUnseededReferenceSet_WhenCurrenciesAreListed_ThenBuiltInSetIsSeededAndReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetFromJsonAsync<CurrencyListEnvelope>("/api/currencies");

        Assert.NotNull(response?.Data);
        Assert.True(response.Data.Currencies.Count >= 170);
        Assert.Equal(
            response.Data.Currencies.OrderBy(currency => currency.Code).Select(currency => currency.Code),
            response.Data.Currencies.Select(currency => currency.Code));
        Assert.Contains(response.Data.Currencies,
            currency => currency is { Code: "BRL", Name: "Brazilian Real", MinorUnitDigits: 2 });
        Assert.Contains(response.Data.Currencies,
            currency => currency is { Code: "JPY", MinorUnitDigits: 0 });

        await using var context = CreateContext();
        Assert.Equal(response.Data.Currencies.Count, await context.Currencies.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenSupportedCode_WhenCurrencyIsRequested_ThenReferenceEntryIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetFromJsonAsync<CurrencyEnvelope>("/api/currencies/brl");

        Assert.Equal("BRL", response!.Data!.Code);
        Assert.Equal("Brazilian Real", response.Data.Name);
        Assert.Equal(2, response.Data.MinorUnitDigits);
    }

    [FunctionalFact]
    public async Task GivenUnknownCode_WhenCurrencyIsRequested_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client);

        var response = await client.GetAsync("/api/currencies/ZZZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenCurrenciesAreRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/currencies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory()
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(databaseOptions =>
                    databaseOptions.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        return new AppDbContext(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static void Authorize(HttpClient client)
    {
        var identity = new FortunaIdentity(
            Guid.NewGuid(),
            (int)HeimdallRoles.User,
            Guid.NewGuid(),
            [])
        {
            DisplayName = "Currency User"
        };
        var configuration = new JwtConfiguration(
            3600,
            Issuer,
            Audience,
            Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
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
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record CurrencyListEnvelope(CurrencyListData? Data);
    private sealed record CurrencyListData(IReadOnlyCollection<CurrencyData> Currencies);
    private sealed record CurrencyEnvelope(CurrencyData? Data);
    private sealed record CurrencyData(string Code, string Name, short MinorUnitDigits);
}
