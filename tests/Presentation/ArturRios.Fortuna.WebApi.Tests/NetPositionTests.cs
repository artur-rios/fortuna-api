using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
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

public sealed class NetPositionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly AsOf = new(2026, 9, 8);
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenMixedLiveHoldings_WhenNetPositionRequested_ThenAsOfIsolationAndConversionApply()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var brlAccount = await CreateAccountAsync(client, "Checking", "BRL", 100m);
        var usdAccount = await CreateAccountAsync(client, "Dollar", "USD", 10m);
        await SeedPortfolioAsync(subject, brlAccount, usdAccount);
        await SeedRateAsync("USD", "BRL", 5m, AsOf.AddDays(-1));
        await SeedForeignAccountAsync();

        // When
        var response = await client.GetAsync(
            $"/api/reports/net-position?displayCurrencyCode=BRL&asOf={AsOf:yyyy-MM-dd}");
        var result = await response.Content.ReadFromJsonAsync<Envelope>();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AsOf, result!.Data!.AsOf);
        Assert.Equal("BRL", result.Data.DisplayCurrencyCode);
        Assert.True(result.Data.IsFullyConverted);
        Assert.Equal(155m, result.Data.Total);
        var groups = result.Data.CurrencyGroups.OrderBy(item => item.SourceCurrencyCode).ToArray();
        Assert.Equal(105m, groups[0].FinancialAccounts);
        Assert.Equal(50m, groups[0].Investments);
        Assert.Equal(25m, groups[0].CreditCards);
        Assert.Equal(130m, groups[0].SourceNet);
        Assert.Equal(10m, groups[1].FinancialAccounts);
        Assert.Equal(-5m, groups[1].Investments);
        Assert.Equal(0m, groups[1].CreditCards);
        Assert.Equal(5m, groups[1].SourceNet);
        Assert.Equal(25m, groups[1].DisplayNet);
        Assert.Equal(5m, groups[1].AppliedRate);
    }

    [FunctionalFact]
    public async Task GivenEmptyNegativeOrMissingRate_WhenNetPositionRequested_ThenAlternativesAreExplicit()
    {
        // Given
        await using var factory = CreateFactory();
        using var empty = factory.CreateClient();
        Authorize(empty, Guid.NewGuid(), HeimdallRoles.User);

        // When
        var emptyResponse = await empty.GetAsync("/api/reports/net-position");
        var emptyResult = await emptyResponse.Content.ReadFromJsonAsync<Envelope>();

        // Then
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Equal(0m, emptyResult!.Data!.Total);
        Assert.Empty(emptyResult.Data.CurrencyGroups);

        // Given
        var subject = Guid.NewGuid();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(owner, "Card", "USD");
        await SeedCardChargeAsync(subject, card, 20m);

        // When
        var negativeResponse = await owner.GetAsync(
            $"/api/reports/net-position?displayCurrencyCode=USD&asOf={AsOf:yyyy-MM-dd}");
        var negative = await negativeResponse.Content.ReadFromJsonAsync<Envelope>();
        var partialResponse = await owner.GetAsync(
            $"/api/reports/net-position?displayCurrencyCode=BRL&asOf={AsOf:yyyy-MM-dd}");
        var partial = await partialResponse.Content.ReadFromJsonAsync<Envelope>();

        // Then
        Assert.Equal(-20m, negative!.Data!.Total);
        Assert.False(partial!.Data!.IsFullyConverted);
        Assert.Null(partial.Data.Total);
        Assert.Equal(FigureConversionMessages.RateUnavailable,
            Assert.Single(partial.Data.CurrencyGroups).UnconvertedReason);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrUnauthorizedCaller_WhenNetPositionRequested_ThenRequestIsRejected()
    {
        // Given
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        using var user = factory.CreateClient();
        Authorize(user, Guid.NewGuid(), HeimdallRoles.User);

        // When
        var anonymousResponse = await anonymous.GetAsync("/api/reports/net-position");
        var administratorResponse = await administrator.GetAsync("/api/reports/net-position");
        var invalidResponse = await user.GetAsync(
            "/api/reports/net-position?displayCurrencyCode=US");

        // Then
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task SeedPortfolioAsync(Guid subject, Guid brlAccountId, Guid usdAccountId)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var usd = await context.Currencies.SingleAsync(item => item.Code == "USD");
        var accounts = await context.FinancialAccounts
            .Include(item => item.Currency)
            .Where(item => item.PublicId == brlAccountId || item.PublicId == usdAccountId)
            .ToDictionaryAsync(item => item.PublicId);
        var category = new Category(user, "General", CreatedAt);
        var card = new CreditCard(user, "Card", "Issuer", brl, 1000m, 20, 5, null,
            CreatedAt);
        var brlInvestment = new Investment(user, "Fund", null, InvestmentType.Fund, brl,
            CreatedAt);
        var usdInvestment = new Investment(user, "Stock", null, InvestmentType.Equity, usd,
            CreatedAt);
        context.AddRange(category, card, brlInvestment, usdInvestment);
        await context.SaveChangesAsync();
        context.AddRange(
            new FinancialTransaction(user, accounts[brlAccountId], category,
                TransactionDirection.Earning, 5m, AsOf, CreatedAt),
            new FinancialTransaction(user, accounts[brlAccountId], category,
                TransactionDirection.Earning, 999m, AsOf.AddDays(1), CreatedAt),
            new FinancialTransaction(user, card, category,
                TransactionDirection.Expense, 25m, AsOf, CreatedAt),
            new InvestmentValuation(brlInvestment, 50m, AsOf, CreatedAt),
            new InvestmentMovement(usdInvestment, InvestmentMovementType.Contribution, 10m,
                AsOf.AddDays(-2), CreatedAt),
            new InvestmentMovement(usdInvestment, InvestmentMovementType.Withdrawal, 15m,
                AsOf, CreatedAt),
            new InvestmentMovement(usdInvestment, InvestmentMovementType.Contribution, 999m,
                AsOf.AddDays(1), CreatedAt));
        await context.SaveChangesAsync();
    }

    private async Task SeedCardChargeAsync(Guid subject, Guid cardId, decimal amount)
    {
        await using var context = CreateContext();
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == cardId);
        var category = new Category(card.User, "Charge", DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        context.FinancialTransactions.Add(new FinancialTransaction(
            card.User, card, category, TransactionDirection.Expense, amount, AsOf,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private async Task SeedForeignAccountAsync()
    {
        using var client = CreateFactory().CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(client, "Foreign", "BRL", 9999m);
    }

    private async Task SeedRateAsync(string baseCode, string quoteCode, decimal rate, DateOnly date)
    {
        await using var context = CreateContext();
        var currencies = await context.Currencies
            .Where(item => item.Code == baseCode || item.Code == quoteCode)
            .ToDictionaryAsync(item => item.Code);
        context.ExchangeRates.Add(new ExchangeRate(
            currencies[baseCode].Id,
            currencies[quoteCode].Id,
            rate,
            date,
            ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        string currencyCode,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = currencyCode,
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateCardAsync(
        HttpClient client,
        string name,
        string currencyCode)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Issuer",
            CurrencyCode = currencyCode,
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 5
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
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
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                    new DateTimeOffset(AsOf.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc))));
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
            DisplayName = "Net Position Owner"
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
        ["FORTUNA_REPORT_MAX_RANGE_DAYS"] = "366",
        ["FORTUNA_REPORT_KEY_TTL_MINUTES"] = "15",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
    private sealed record Envelope(NetPositionData? Data);
    private sealed record NetPositionData(
        DateOnly AsOf,
        string DisplayCurrencyCode,
        decimal? Total,
        bool IsFullyConverted,
        IReadOnlyCollection<CurrencyGroupData> CurrencyGroups);
    private sealed record CurrencyGroupData(
        string SourceCurrencyCode,
        decimal FinancialAccounts,
        decimal Investments,
        decimal CreditCards,
        decimal SourceNet,
        decimal? DisplayNet,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
