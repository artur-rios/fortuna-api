using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
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

public sealed class CommittedObligationsTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly Today = new(2026, 9, 8);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenMixedCommitments_WhenListed_ThenOnlyOwnedFactsAreConvertedAndOrdered()
    {
        // Given
        var subject = Guid.NewGuid();
        await SeedCommitmentsAsync(subject);
        await SeedOtherOwnerCommitmentAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        // When
        var response = await client.GetAsync(
            "/api/projections/commitments?horizonDays=40&displayCurrencyCode=BRL");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToArray();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, items.Length);
        Assert.Equal(110m, data.GetProperty("total").GetDecimal());
        Assert.True(data.GetProperty("isFullyConverted").GetBoolean());
        Assert.Equal(2, data.GetProperty("periods").GetArrayLength());
        Assert.Single(data.GetProperty("rates").EnumerateArray());
        Assert.True(items[0].GetProperty("isOverdue").GetBoolean());
        Assert.Equal(2, items[0].GetProperty("daysOverdue").GetInt32());
        Assert.Equal(100m, items[0].GetProperty("displayAmount").GetDecimal());
        Assert.False(items[1].GetProperty("isOverdue").GetBoolean());
        Assert.Equal(10m, items[1].GetProperty("displayAmount").GetDecimal());
        Assert.Equal(Today.AddDays(11).ToString("yyyy-MM-dd"),
            items[1].GetProperty("cycleStart").GetString());
        Assert.Equal(Today.AddDays(25).ToString("yyyy-MM-dd"),
            items[1].GetProperty("cycleEnd").GetString());
    }

    [FunctionalFact]
    public async Task GivenEmptyInvalidOrUnauthorizedRequest_WhenListed_ThenAlternativesAreExplicit()
    {
        // Given
        var subject = Guid.NewGuid();
        await SeedEmptyProfileAsync(subject);
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        using var outsider = factory.CreateClient();
        Authorize(outsider, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        // When
        var empty = await owner.GetAsync("/api/projections/commitments?horizonDays=30");
        var emptyDocument = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        var invalid = await owner.GetAsync("/api/projections/commitments?horizonDays=367");
        var missingOwner = await outsider.GetAsync(
            "/api/projections/commitments?horizonDays=30");
        var outsiderDocument = JsonDocument.Parse(
            await missingOwner.Content.ReadAsStringAsync());
        var anonymousResponse = await anonymous.GetAsync(
            "/api/projections/commitments?horizonDays=30");
        var forbidden = await administrator.GetAsync(
            "/api/projections/commitments?horizonDays=30");

        // Then
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(0m, emptyDocument.RootElement.GetProperty("data")
            .GetProperty("total").GetDecimal());
        Assert.Empty(emptyDocument.RootElement.GetProperty("data")
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(CommittedObligationMessages.HorizonMaximum(366),
            await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, missingOwner.StatusCode);
        Assert.Equal(0m, outsiderDocument.RootElement.GetProperty("data")
            .GetProperty("total").GetDecimal());
        Assert.Empty(outsiderDocument.RootElement.GetProperty("data")
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task SeedCommitmentsAsync(Guid subject)
    {
        await using var context = CreateContext();
        var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var usd = await context.Currencies.SingleAsync(item => item.Code == "USD");
        var user = new UserProfile(subject, "Commitment Owner", brl, DateTimeOffset.UtcNow);
        var account = new FinancialAccount(user, "Checking", null,
            FinancialAccountType.Checking, brl, 100m, DateTimeOffset.UtcNow);
        var brlCard = new CreditCard(user, "BRL Card", "Issuer", brl, 1000m,
            20, 5, null, DateTimeOffset.UtcNow);
        var usdCard = new CreditCard(user, "USD Card", "Issuer", usd, 1000m,
            20, 5, null, DateTimeOffset.UtcNow);
        var category = new Category(user, "General", DateTimeOffset.UtcNow);
        context.AddRange(user, account, brlCard, usdCard, category);
        await context.SaveChangesAsync();

        var plan = new InstallmentPlan(
            brlCard, 20m, 2, Today.AddDays(20), DateTimeOffset.UtcNow);
        var upcoming = new FinancialTransaction(user, brlCard, category,
            TransactionDirection.Expense, 10m, Today.AddDays(20), DateTimeOffset.UtcNow);
        var outsideHorizon = new FinancialTransaction(user, brlCard, category,
            TransactionDirection.Expense, 10m, Today.AddDays(60), DateTimeOffset.UtcNow);
        plan.AddInstallment(upcoming, 1, DateTimeOffset.UtcNow);
        plan.AddInstallment(outsideHorizon, 2, DateTimeOffset.UtcNow);
        var openStatement = new CreditCardStatement(brlCard, new BillingCycle(
            Today.AddDays(11), Today.AddDays(25), Today.AddDays(25), Today.AddDays(30)),
            DateTimeOffset.UtcNow);
        upcoming.AssignToStatement(openStatement, false, DateTimeOffset.UtcNow);

        var overdueStatement = new CreditCardStatement(usdCard, new BillingCycle(
            Today.AddDays(-30), Today.AddDays(-7), Today.AddDays(-7), Today.AddDays(-2)),
            DateTimeOffset.UtcNow);
        overdueStatement.RecalculatePurchaseTotal(20m, DateTimeOffset.UtcNow);
        overdueStatement.Close(DateTimeOffset.UtcNow);

        var recurring = new RecurringTransaction(user, account, null, category,
            TransactionDirection.Expense, 500m, RecurrenceFrequency.Weekly,
            Today.AddDays(7), Today.AddDays(7), DateTimeOffset.UtcNow);
        context.AddRange(plan, openStatement, overdueStatement, recurring,
            new ExchangeRate(usd.Id, brl.Id, 5m, Today.AddDays(-3),
                ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

    private async Task SeedOtherOwnerCommitmentAsync()
    {
        await using var context = CreateContext();
        var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(Guid.NewGuid(), "Other Owner", brl, DateTimeOffset.UtcNow);
        var card = new CreditCard(user, "Other Card", "Issuer", brl, 1000m,
            20, 5, null, DateTimeOffset.UtcNow);
        var statement = new CreditCardStatement(card, new BillingCycle(
            Today.AddDays(-20), Today.AddDays(-5), Today.AddDays(-5), Today.AddDays(2)),
            DateTimeOffset.UtcNow);
        statement.RecalculatePurchaseTotal(999m, DateTimeOffset.UtcNow);
        statement.Close(DateTimeOffset.UtcNow);
        context.AddRange(user, card, statement);
        await context.SaveChangesAsync();
    }

    private async Task SeedEmptyProfileAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        context.UserProfiles.Add(new UserProfile(
            subject, "Empty Owner", currency, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
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
                    new DateTimeOffset(Today.ToDateTime(
                        new TimeOnly(12, 0), DateTimeKind.Utc))));
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
        return new AppDbContext(options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Commitment Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
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
        ["FORTUNA_PROJECTION_MAX_HORIZON_DAYS"] = "366",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
