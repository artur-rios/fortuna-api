using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Auditing;
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

public sealed class ManualExchangeRateTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenSupportedPair_WhenManualRateIsRecorded_ThenItTakesPrecedenceAndIsAudited()
    {
        var date = new DateOnly(2026, 8, 28);
        await AddPublishedRateAsync("USD", "BRL", 5.1m, date);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("usd", "brl", 5.25m, date));
        var envelope = await response.Content.ReadFromJsonAsync<RateEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.Equal("USD", envelope.Data.BaseCurrencyCode);
        Assert.Equal("BRL", envelope.Data.QuoteCurrencyCode);
        Assert.Equal(5.25m, envelope.Data.Rate);
        Assert.Equal(ExchangeRateSource.Manual, envelope.Data.Source);
        Assert.True(envelope.Data.TakesPrecedence);
        Assert.False(envelope.Data.ReplacedExisting);
        await using var context = CreateContext();
        var rates = await context.ExchangeRates
            .Where(rate => rate.RateDate == date)
            .OrderBy(rate => rate.Source)
            .ToArrayAsync();
        Assert.Collection(
            rates,
            rate => Assert.Equal((ExchangeRateSource.Published, 5.1m), (rate.Source, rate.Rate)),
            rate => Assert.Equal((ExchangeRateSource.Manual, 5.25m), (rate.Source, rate.Rate)));
        var actorId = await context.UserProfiles
            .Where(profile => profile.ExternalSubject == subject.ToString("D"))
            .Select(profile => profile.PublicId)
            .SingleAsync();
        var audit = await context.AuditEntries
            .SingleAsync(entry => entry.Operation == "RecordManualExchangeRateCommand" &&
                entry.ActorUserId == actorId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Null(audit.Reason);
    }

    [FunctionalTheory]
    [InlineData("0")]
    [InlineData("-0.00000001")]
    public async Task GivenNonPositiveRate_WhenRecorded_ThenBadRequestIsAudited(string value)
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("USD", "BRL", decimal.Parse(value), new DateOnly(2026, 8, 30)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == new DateOnly(2026, 8, 30)));
        var actorId = await context.UserProfiles
            .Where(profile => profile.ExternalSubject == subject.ToString("D"))
            .Select(profile => profile.PublicId)
            .SingleAsync();
        var audit = await context.AuditEntries
            .SingleAsync(entry => entry.Operation == "RecordManualExchangeRateCommand" &&
                entry.ActorUserId == actorId);
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
        Assert.Equal("Rate must be greater than zero.", audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenExcessRatePrecision_WhenRecorded_ThenBadRequestStoresNoRate()
    {
        var date = new DateOnly(2026, 8, 27);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("USD", "BRL", 1.123456789m, date));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == date));
    }

    [FunctionalFact]
    public async Task GivenSameCurrencies_WhenRecorded_ThenBadRequestStoresNoRate()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("usd", "USD", 1m, new DateOnly(2026, 8, 31)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == new DateOnly(2026, 8, 31)));
    }

    [FunctionalTheory]
    [InlineData("ZZZ", "BRL", "ZZZ", "2026-09-01")]
    [InlineData("USD", "ZZZ", "ZZZ", "2026-09-02")]
    public async Task GivenUnknownCurrency_WhenRecorded_ThenBadRequestNamesUnknownCode(
        string baseCode,
        string quoteCode,
        string unknownCode,
        string dateValue)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command(baseCode, quoteCode, 5.25m, DateOnly.Parse(dateValue)));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"Unknown currency code '{unknownCode}'.", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenExistingManualRate_WhenRecorded_ThenOneRowIsReplacedAndBothWritesAreAudited()
    {
        var date = new DateOnly(2026, 8, 29);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var created = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("EUR", "BRL", 6.1m, date));
        var replaced = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("EUR", "BRL", 6.2m, date));
        var envelope = await replaced.Content.ReadFromJsonAsync<RateEnvelope>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.True(envelope!.Data!.ReplacedExisting);
        await using var context = CreateContext();
        var manual = await context.ExchangeRates.SingleAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == date);
        Assert.Equal(6.2m, manual.Rate);
        var actorId = await context.UserProfiles
            .Where(profile => profile.ExternalSubject == subject.ToString("D"))
            .Select(profile => profile.PublicId)
            .SingleAsync();
        Assert.Equal(2, await context.AuditEntries.CountAsync(entry =>
            entry.Operation == "RecordManualExchangeRateCommand" &&
            entry.Outcome == AuditOutcome.Succeeded &&
            entry.ActorUserId == actorId));
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenManualRateIsRecorded_ThenUnauthorizedStoresNoRate()
    {
        var date = new DateOnly(2026, 9, 3);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("USD", "BRL", 5.25m, date));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == date));
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenManualRateIsRecorded_ThenForbiddenStoresNoRate()
    {
        var date = new DateOnly(2026, 9, 4);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("USD", "BRL", 5.25m, date));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == date));
    }

    [FunctionalFact]
    public async Task GivenLocalAccountToken_WhenManualRateIsRecorded_ThenRequestSucceedsOffline()
    {
        var date = new DateOnly(2026, 9, 5);
        await using var factory = CreateFactory(localAuthenticationEnabled: true);
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/local-accounts", new
        {
            DisplayName = "Local Rate User",
            Secret = "correct-horse-battery-staple",
            StorageMode = LocalAccountStorageMode.InMemory
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var authenticated = await client.PostAsJsonAsync("/api/local-accounts/authenticate", new
        {
            Name = "Local Rate User",
            Secret = "correct-horse-battery-staple"
        });
        var token = (await authenticated.Content.ReadFromJsonAsync<TokenEnvelope>())!.Data!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/exchange-rates",
            Command("USD", "BRL", 5.3m, date));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var context = CreateContext();
        Assert.True(await context.ExchangeRates.AnyAsync(rate =>
            rate.Source == ExchangeRateSource.Manual && rate.RateDate == date));
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Operation == "RecordManualExchangeRateCommand" && entry.ActorUserId != null));
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task AddPublishedRateAsync(string baseCode, string quoteCode, decimal value, DateOnly date)
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
            ExchangeRateSource.Published));
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
            DisplayName = "Rate User"
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

    private static object Command(
        string baseCode,
        string quoteCode,
        decimal rate,
        DateOnly date) => new
        {
            BaseCurrencyCode = baseCode,
            QuoteCurrencyCode = quoteCode,
            Rate = rate,
            RateDate = date
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

    private sealed record RateEnvelope(RateData? Data);
    private sealed record RateData(
        string BaseCurrencyCode,
        string QuoteCurrencyCode,
        decimal Rate,
        DateOnly RateDate,
        ExchangeRateSource Source,
        bool TakesPrecedence,
        bool ReplacedExisting);
    private sealed record TokenEnvelope(TokenData? Data);
    private sealed record TokenData(string Token);
}
