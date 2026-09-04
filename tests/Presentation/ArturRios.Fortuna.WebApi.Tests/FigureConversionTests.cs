using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
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

public sealed class FigureConversionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenMixedCurrencies_WhenFigureIsConverted_ThenManualRatesAndAttributionAreReturned()
    {
        var date = new DateOnly(2040, 1, 10);
        await AddRateAsync("USD", "BRL", 5m, date, ExchangeRateSource.Published);
        await AddRateAsync("USD", "BRL", 5.5m, date, ExchangeRateSource.Manual);
        await AddRateAsync("EUR", "BRL", 6m, date, ExchangeRateSource.Published);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await using var beforeContext = CreateContext();
        var rateCount = await beforeContext.ExchangeRates.CountAsync();
        var auditCount = await beforeContext.AuditEntries.CountAsync();

        var response = await client.PostAsJsonAsync("/api/exchange-rates/convert", Request(
            date,
            null,
            (1m, "usd"),
            (1m, "USD"),
            (2m, "EUR"),
            (3m, "BRL")));
        var envelope = await response.Content.ReadFromJsonAsync<FigureEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.Equal("BRL", envelope.Data.DisplayCurrencyCode);
        Assert.True(envelope.Data.IsFullyConverted);
        Assert.Equal(26m, envelope.Data.Total);
        Assert.Collection(
            envelope.Data.Groups.OrderBy(group => group.SourceCurrencyCode),
            group =>
            {
                Assert.Equal("BRL", group.SourceCurrencyCode);
                Assert.Equal(3m, group.DisplayAmount);
                Assert.Null(group.AppliedRate);
            },
            group =>
            {
                Assert.Equal("EUR", group.SourceCurrencyCode);
                Assert.Equal(12m, group.DisplayAmount);
                Assert.Equal(6m, group.AppliedRate);
                Assert.Equal(date, group.RateDate);
                Assert.Equal(ExchangeRateSource.Published, group.RateSource);
            },
            group =>
            {
                Assert.Equal("USD", group.SourceCurrencyCode);
                Assert.Equal(2m, group.SourceAmount);
                Assert.Equal(11m, group.DisplayAmount);
                Assert.Equal(5.5m, group.AppliedRate);
                Assert.Equal(date, group.RateDate);
                Assert.Equal(ExchangeRateSource.Manual, group.RateSource);
            });
        await using var afterContext = CreateContext();
        Assert.Equal(rateCount, await afterContext.ExchangeRates.CountAsync());
        Assert.Equal(auditCount, await afterContext.AuditEntries.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenNoRateOnFigureDate_WhenConverted_ThenLatestPriorRateAndDateAreReturned()
    {
        var rateDate = new DateOnly(2040, 2, 1);
        var figureDate = rateDate.AddDays(4);
        await AddRateAsync("AUD", "BRL", 3.25m, rateDate, ExchangeRateSource.Published);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(figureDate, "BRL", (2m, "AUD")));
        var data = (await response.Content.ReadFromJsonAsync<FigureEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = Assert.Single(data.Groups);
        Assert.Equal(6.5m, data.Total);
        Assert.Equal(rateDate, group.RateDate);
        Assert.Equal(3.25m, group.AppliedRate);
    }

    [FunctionalFact]
    public async Task GivenNoRateEverStored_WhenConverted_ThenFigureIsSplitWithoutInventedTotal()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 3, 1), "BRL", (10m, "NZD"), (20m, "BRL")));
        var data = (await response.Content.ReadFromJsonAsync<FigureEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(data.IsFullyConverted);
        Assert.Null(data.Total);
        var unresolved = Assert.Single(data.Groups, group => group.SourceCurrencyCode == "NZD");
        Assert.Null(unresolved.DisplayAmount);
        Assert.Equal("No exchange rate has ever been stored for this currency pair.", unresolved.UnconvertedReason);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedDisplayCurrency_WhenConverted_ThenBadRequestNamesIt()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 4, 1), "ZZZ", (1m, "BRL")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown currency code 'ZZZ'.", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenOnlyDisplayCurrency_WhenConverted_ThenNoRateIsReported()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 5, 1), "JPY", (100.4m, "JPY"), (0.1m, "jpy")));
        var data = (await response.Content.ReadFromJsonAsync<FigureEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(101m, data.Total);
        var group = Assert.Single(data.Groups);
        Assert.Null(group.AppliedRate);
        Assert.Null(group.RateDate);
        Assert.Null(group.RateSource);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenFigureIsRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 6, 1), "BRL", (1m, "BRL")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenAdministratorToken_WhenFigureIsRequested_ThenForbiddenIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 7, 1), "BRL", (1m, "BRL")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenLocalAccount_WhenDefaultCurrencyFigureIsRequested_ThenItSucceedsOffline()
    {
        await using var factory = CreateFactory(localAuthenticationEnabled: true);
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/local-accounts", new
        {
            DisplayName = "Local Figure User",
            Secret = "correct-horse-battery-staple",
            StorageMode = LocalAccountStorageMode.InMemory
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var authenticated = await client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            Name = "Local Figure User",
            Secret = "correct-horse-battery-staple"
        });
        var token = (await authenticated.Content.ReadFromJsonAsync<TokenEnvelope>())!.Data!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates/convert",
            Request(new DateOnly(2040, 8, 1), null, (10m, "BRL")));
        var data = (await response.Content.ReadFromJsonAsync<FigureEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("BRL", data.DisplayCurrencyCode);
        Assert.Equal(10m, data.Total);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task AddRateAsync(
        string baseCode,
        string quoteCode,
        decimal value,
        DateOnly date,
        ExchangeRateSource source)
    {
        await using var context = CreateContext();
        var currencies = await context.Currencies
            .Where(currency => currency.Code == baseCode || currency.Code == quoteCode)
            .ToDictionaryAsync(currency => currency.Code);
        context.ExchangeRates.Add(new ExchangeRate(
            currencies[baseCode].Id,
            currencies[quoteCode].Id,
            value,
            date,
            source));
        await context.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(bool localAuthenticationEnabled = false)
    {
        foreach (var setting in ValidSettings(localAuthenticationEnabled))
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
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
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

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Figure User"
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

    private static object Request(
        DateOnly figureDate,
        string? displayCurrencyCode,
        params (decimal Amount, string CurrencyCode)[] amounts) => new
        {
            FigureDate = figureDate,
            DisplayCurrencyCode = displayCurrencyCode,
            Amounts = amounts.Select(amount => new
            {
                amount.Amount,
                amount.CurrencyCode
            })
        };

    private static Dictionary<string, string?> ValidSettings(bool localAuthenticationEnabled) => new()
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
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = localAuthenticationEnabled.ToString(),
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_RATES_SOURCE_BASE_URL"] = null,
        ["FORTUNA_RATES_SYNC_CRON"] = null,
        ["FORTUNA_RATES_CURRENCIES"] = null
    };

    private sealed record FigureEnvelope(FigureData? Data);
    private sealed record FigureData(
        string DisplayCurrencyCode,
        DateOnly FigureDate,
        decimal? Total,
        bool IsFullyConverted,
        IReadOnlyCollection<GroupData> Groups);
    private sealed record GroupData(
        string SourceCurrencyCode,
        decimal SourceAmount,
        decimal? DisplayAmount,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
    private sealed record TokenEnvelope(TokenData? Data);
    private sealed record TokenData(string Token);
}
