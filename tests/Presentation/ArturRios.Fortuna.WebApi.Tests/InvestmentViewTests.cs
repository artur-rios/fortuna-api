using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
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

public sealed class InvestmentViewTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLatestValuation_WhenViewed_ThenOnlyLaterMovementsAdjustPosition()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investment = await SeedInvestmentAsync(client, subject, "Position", "BRL");
        await SeedMovementsAsync(
            investment.Id,
            (InvestmentMovementType.Contribution, 100m, Today().AddDays(-3)),
            (InvestmentMovementType.Yield, 10m, Today().AddDays(-1)),
            (InvestmentMovementType.Fee, 4m, Today()));
        await SeedValuationsAsync(
            investment.Id,
            (90m, Today().AddDays(-2)),
            (120m, Today().AddDays(-1)));

        var response = await client.GetAsync($"/api/investments/{investment.Id}");
        var envelope = await response.Content.ReadFromJsonAsync<InvestmentEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(116m, envelope?.Data?.Position);
        Assert.True(envelope?.Data?.IsIndependentlyValued);
        Assert.Equal(120m, envelope?.Data?.LatestValuationValue);
        Assert.Equal(Today().AddDays(-1), envelope?.Data?.LatestValuationDate);
        Assert.Contains(InvestmentMessages.RetrievedSuccessfully, envelope!.Messages);
    }

    [FunctionalFact]
    public async Task GivenNoValuation_WhenViewed_ThenAllLiveMovementsFormPosition()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investment = await SeedInvestmentAsync(client, subject, "Unvalued", "BRL");
        await SeedMovementsAsync(
            investment.Id,
            (InvestmentMovementType.Contribution, 50m, Today().AddDays(-1)),
            (InvestmentMovementType.Withdrawal, 7m, Today()));

        var envelope = await client.GetFromJsonAsync<InvestmentEnvelope>(
            $"/api/investments/{investment.Id}");

        Assert.Equal(43m, envelope?.Data?.Position);
        Assert.False(envelope?.Data?.IsIndependentlyValued);
        Assert.Null(envelope?.Data?.LatestValuationValue);
    }

    [FunctionalFact]
    public async Task GivenFiltersAndDisplayCurrency_WhenListed_ThenOwnedPositionsAreConverted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var first = await SeedInvestmentAsync(client, subject, "Reserve One", "USD");
        var second = await SeedInvestmentAsync(client, subject, "Reserve Two", "USD");
        var native = await SeedInvestmentAsync(client, subject, "Reserve Native", "BRL");
        using var foreign = factory.CreateClient();
        var foreignSubject = Guid.NewGuid();
        Authorize(foreign, foreignSubject, HeimdallRoles.User);
        await SeedInvestmentAsync(foreign, foreignSubject, "Reserve Foreign", "USD");
        await SeedValuationsAsync(first.Id, (10m, Today()));
        await SeedValuationsAsync(second.Id, (20m, Today()));
        await SeedValuationsAsync(native.Id, (5m, Today()));
        await SeedRateAsync("USD", "BRL", 5m, Today());

        var response = await client.GetAsync(
            "/api/investments?Instrument=reserve&Institution=BROK&InvestmentType=3" +
            "&DisplayCurrencyCode=brl&FigureDate=" + Today().ToString("yyyy-MM-dd") +
            "&SortBy=Position&Descending=true");
        var page = await response.Content.ReadFromJsonAsync<InvestmentPage>();
        var filtered = await client.GetFromJsonAsync<InvestmentPage>(
            "/api/investments?Instrument=reserve&CurrencyCode=usd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, page?.TotalItems);
        Assert.Equal([20m, 10m, 5m], page!.Data.Select(item => item.Position));
        Assert.Equal([100m, 50m, 5m], page.Data.Select(item => item.DisplayPosition));
        Assert.All(page.Data.Take(2), item =>
        {
            Assert.Equal("USD", item.CurrencyCode);
            Assert.Equal("BRL", item.DisplayCurrencyCode);
            Assert.Equal(5m, item.AppliedRate);
            Assert.Equal(ExchangeRateSource.Manual, item.RateSource);
        });
        Assert.Equal("BRL", page.Data[2].CurrencyCode);
        Assert.Null(page.Data[2].AppliedRate);
        Assert.Equal(2, filtered?.TotalItems);
        Assert.All(filtered!.Data, item => Assert.Equal("USD", item.CurrencyCode));
    }

    [FunctionalFact]
    public async Task GivenUnavailableRate_WhenListed_ThenNativePositionIsStillReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investment = await SeedInvestmentAsync(client, subject, "No Rate", "EUR");
        await SeedValuationsAsync(investment.Id, (25m, Today()));

        var page = await client.GetFromJsonAsync<InvestmentPage>(
            "/api/investments?Instrument=No%20Rate&DisplayCurrencyCode=BRL");
        var item = Assert.Single(page!.Data);

        Assert.Equal(25m, item.Position);
        Assert.Null(item.DisplayPosition);
        Assert.Equal(FigureConversionMessages.RateUnavailable, item.UnconvertedReason);
    }

    [FunctionalFact]
    public async Task GivenValuationPeriod_WhenHistoryViewed_ThenResultsAreFilteredAndEmptyIsValid()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investment = await SeedInvestmentAsync(client, subject, "History", "BRL");
        await SeedValuationsAsync(
            investment.Id,
            (10m, Today().AddDays(-2)),
            (20m, Today().AddDays(-1)),
            (30m, Today()));

        var page = await client.GetFromJsonAsync<ValuationPage>(
            $"/api/investments/{investment.Id}/valuations?From={Today().AddDays(-1):yyyy-MM-dd}" +
            $"&To={Today():yyyy-MM-dd}&SortBy=Value&Descending=false");
        var empty = await client.GetFromJsonAsync<ValuationPage>(
            $"/api/investments/{investment.Id}/valuations?To={Today().AddDays(-3):yyyy-MM-dd}");

        Assert.Equal([20m, 30m], page!.Data.Select(item => item.Value));
        Assert.Contains(InvestmentMessages.ValuationHistoryRetrievedSuccessfully, page.Messages);
        Assert.Empty(empty!.Data);
        Assert.Equal(0, empty.TotalItems);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrDeletedInvestment_WhenViewed_ThenNotFoundIsIndistinguishable()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        var live = await SeedInvestmentAsync(owner, ownerSubject, "Private", "BRL");
        var deleted = await SeedInvestmentAsync(owner, ownerSubject, "Deleted", "BRL", true);
        await EnsureProfileAsync(other);

        var foreign = await other.GetAsync($"/api/investments/{live.Id}");
        var missing = await other.GetAsync($"/api/investments/{Guid.NewGuid()}");
        var hidden = await owner.GetAsync($"/api/investments/{deleted.Id}");
        var foreignHistory = await other.GetAsync($"/api/investments/{live.Id}/valuations");

        Assert.All([foreign, missing, hidden, foreignHistory], response =>
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [FunctionalFact]
    public async Task GivenInvalidFilterOrAccess_WhenListed_ThenRequestIsRejected()
    {
        await using var factory = CreateFactory();
        using var user = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(user, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var filter = await user.GetAsync("/api/investments?Balance=10");
        var invalidPeriod = await user.GetAsync(
            $"/api/investments/{Guid.NewGuid()}/valuations?From=2026-09-02&To=2026-09-01");
        var anonymousResponse = await anonymous.GetAsync("/api/investments");
        var administratorResponse = await administrator.GetAsync("/api/investments");

        Assert.Equal(HttpStatusCode.BadRequest, filter.StatusCode);
        Assert.Contains("Balance", await filter.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPeriod.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<SeededInvestment> SeedInvestmentAsync(
        HttpClient client,
        Guid subject,
        string instrument,
        string currencyCode,
        bool deleted = false)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == currencyCode);
        var investment = new Investment(
            user,
            $"{instrument} {Guid.NewGuid():N}",
            "Broker",
            InvestmentType.Fund,
            currency,
            DateTimeOffset.UtcNow);
        if (deleted)
        {
            investment.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.Investments.Add(investment);
        await context.SaveChangesAsync();
        return new SeededInvestment(investment.PublicId);
    }

    private async Task SeedMovementsAsync(
        Guid investmentId,
        params (InvestmentMovementType Type, decimal Amount, DateOnly OccurredOn)[] records)
    {
        await using var context = CreateContext();
        var investment = await context.Investments.SingleAsync(item => item.PublicId == investmentId);
        context.InvestmentMovements.AddRange(records.Select(record => new InvestmentMovement(
            investment,
            record.Type,
            record.Amount,
            record.OccurredOn,
            DateTimeOffset.UtcNow)));
        await context.SaveChangesAsync();
    }

    private async Task SeedValuationsAsync(
        Guid investmentId,
        params (decimal Value, DateOnly ValuedOn)[] records)
    {
        await using var context = CreateContext();
        var investment = await context.Investments.SingleAsync(item => item.PublicId == investmentId);
        context.InvestmentValuations.AddRange(records.Select(record => new InvestmentValuation(
            investment,
            record.Value,
            record.ValuedOn,
            DateTimeOffset.UtcNow)));
        await context.SaveChangesAsync();
    }

    private async Task SeedRateAsync(
        string baseCode,
        string quoteCode,
        decimal rate,
        DateOnly date)
    {
        await using var context = CreateContext();
        var currencies = await context.Currencies.Where(item =>
            item.Code == baseCode || item.Code == quoteCode).ToDictionaryAsync(item => item.Code);
        context.ExchangeRates.Add(new ExchangeRate(
            currencies[baseCode].Id,
            currencies[quoteCode].Id,
            rate,
            date,
            ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

    private static async Task EnsureProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Investment Owner"
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
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record SeededInvestment(Guid Id);
    private sealed record InvestmentEnvelope(
        InvestmentData? Data,
        IReadOnlyCollection<string> Messages);
    private sealed record InvestmentPage(
        IReadOnlyList<InvestmentData> Data,
        IReadOnlyCollection<string> Messages,
        int TotalItems);
    private sealed record ValuationPage(
        IReadOnlyList<ValuationData> Data,
        IReadOnlyCollection<string> Messages,
        int TotalItems);
    private sealed record InvestmentData(
        Guid Id,
        string Instrument,
        string? Institution,
        InvestmentType InvestmentType,
        string CurrencyCode,
        decimal Position,
        bool IsIndependentlyValued,
        decimal? LatestValuationValue,
        DateOnly? LatestValuationDate,
        string? DisplayCurrencyCode,
        decimal? DisplayPosition,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
    private sealed record ValuationData(Guid Id, decimal Value, DateOnly ValuedOn);
}
