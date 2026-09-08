using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
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

public sealed class CashFlowProjectionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly Today = new(2026, 9, 8);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLiveInputs_WhenCashFlowProjectedTwice_ThenItRecomputesWithoutPersistingFigures()
    {
        // Given
        var subject = Guid.NewGuid();
        await SeedProjectionAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var transactionCount = await TransactionCountAsync();

        // When
        var first = await client.GetAsync(
            "/api/projections/cash-flow?horizonDays=40&includeEstimate=true");
        var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstBalance = firstJson.RootElement.GetProperty("data")
            .GetProperty("startingBalance").GetDecimal();
        await AddCurrentEarningAsync(subject, 7m);
        var second = await client.GetAsync(
            "/api/projections/cash-flow?horizonDays=40&includeEstimate=true");
        var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondBalance = secondJson.RootElement.GetProperty("data")
            .GetProperty("startingBalance").GetDecimal();

        // Then
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(7m, secondBalance - firstBalance);
        var periods = firstJson.RootElement.GetProperty("data").GetProperty("periods");
        Assert.Equal(2, periods.GetArrayLength());
        var kinds = periods.EnumerateArray()
            .SelectMany(period => period.GetProperty("figures").EnumerateArray())
            .Select(figure => figure.GetProperty("kind").GetInt32())
            .ToArray();
        Assert.Contains(1, kinds); // recorded
        Assert.Contains(2, kinds); // projected recurring rule
        Assert.Contains(3, kinds); // committed installment / statement
        Assert.Contains(4, kinds); // historical estimate
        Assert.Equal(transactionCount + 1, await TransactionCountAsync());
    }

    [FunctionalFact]
    public async Task GivenEmptyInvalidOrUnauthorizedRequest_WhenCashFlowProjected_ThenAlternativesAreExplicit()
    {
        // Given
        var subject = Guid.NewGuid();
        await SeedEmptyProfileAsync(subject);
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        // When
        var empty = await owner.GetAsync("/api/projections/cash-flow?horizonDays=30");
        var emptyBody = await empty.Content.ReadAsStringAsync();
        var invalid = await owner.GetAsync("/api/projections/cash-flow?horizonDays=367");
        var anonymousResponse = await anonymous.GetAsync(
            "/api/projections/cash-flow?horizonDays=30");
        var forbidden = await administrator.GetAsync(
            "/api/projections/cash-flow?horizonDays=30");

        // Then
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains(CashFlowProjectionMessages.NoProjectionInputs, emptyBody);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(CashFlowProjectionMessages.HorizonMaximum(366),
            await invalid.Content.ReadAsStringAsync());
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

    private async Task SeedProjectionAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, "Projection Owner", currency, DateTimeOffset.UtcNow);
        var account = new FinancialAccount(user, "Checking", null,
            FinancialAccountType.Checking, currency, 100m, DateTimeOffset.UtcNow);
        var card = new CreditCard(user, "Card", "Issuer", currency, 1000m,
            20, 5, null, DateTimeOffset.UtcNow);
        var category = new Category(user, "General", DateTimeOffset.UtcNow);
        context.AddRange(user, account, card, category);
        await context.SaveChangesAsync();

        var history = new FinancialTransaction(user, account, category,
            TransactionDirection.Expense, 90m, Today.AddDays(-89), DateTimeOffset.UtcNow);
        var recurring = new RecurringTransaction(user, account, null, category,
            TransactionDirection.Expense, 10m, RecurrenceFrequency.Weekly,
            Today.AddDays(7), Today.AddDays(7), DateTimeOffset.UtcNow);
        var plan = new InstallmentPlan(card, 10m, 2, Today.AddDays(12), DateTimeOffset.UtcNow);
        var firstInstallment = new FinancialTransaction(user, card, category,
            TransactionDirection.Expense, 5m, Today.AddDays(12), DateTimeOffset.UtcNow);
        var secondInstallment = new FinancialTransaction(user, card, category,
            TransactionDirection.Expense, 5m, Today.AddDays(50), DateTimeOffset.UtcNow);
        plan.AddInstallment(firstInstallment, 1, DateTimeOffset.UtcNow);
        plan.AddInstallment(secondInstallment, 2, DateTimeOffset.UtcNow);
        var statement = new CreditCardStatement(card, new BillingCycle(
            Today.AddDays(-20), Today.AddDays(-5), Today.AddDays(-5), Today.AddDays(10)),
            DateTimeOffset.UtcNow);
        statement.RecalculatePurchaseTotal(20m, DateTimeOffset.UtcNow);
        statement.Close(DateTimeOffset.UtcNow);
        context.AddRange(history, recurring, plan, statement);
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

    private async Task AddCurrentEarningAsync(Guid subject, decimal amount)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var account = await context.FinancialAccounts
            .Include(item => item.Currency)
            .SingleAsync(item => item.UserId == user.Id);
        var category = await context.Categories.SingleAsync(item => item.UserId == user.Id);
        context.FinancialTransactions.Add(new FinancialTransaction(
            user, account, category, TransactionDirection.Earning,
            amount, Today, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private async Task<int> TransactionCountAsync()
    {
        await using var context = CreateContext();
        return await context.FinancialTransactions.CountAsync();
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
            DisplayName = "Projection Owner"
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
