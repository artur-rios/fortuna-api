using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
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

public sealed class InstallmentPlanTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenUnevenPurchase_WhenRecorded_ThenInstallmentsSumExactlyAcrossCycles()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Installment card", "BRL");
        var category = await SeedCategoryAsync(subject, "Installments");

        var response = await client.PostAsJsonAsync(
            "/api/installment-plans",
            Request(card, category, 100m, 3));
        var plan = (await response.Content.ReadFromJsonAsync<PlanEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([33.34m, 33.33m, 33.33m],
            plan.Installments.Select(item => item.Amount).ToArray());
        Assert.Equal(100m, plan.Installments.Sum(item => item.Amount));
        Assert.Equal([1, 2, 3], plan.Installments.Select(item => item.Number).ToArray());
        Assert.Equal(
            [Today, Today.AddMonths(1), Today.AddMonths(2)],
            plan.Installments.Select(item => item.OccurredOn).ToArray());
        Assert.Equal(3, plan.Installments.Select(item => item.StatementId).Distinct().Count());

        var read = await client.GetFromJsonAsync<PlanEnvelope>(
            $"/api/installment-plans/{plan.Id}");
        var individualChange = await client.PutAsJsonAsync(
            $"/api/transactions/{plan.Installments.First().TransactionId}",
            new
            {
                CategoryId = category,
                Direction = TransactionDirection.Expense,
                Amount = 1m,
                OccurredOn = Today,
                Tags = Array.Empty<string>()
            });
        Assert.Equal(plan.Id, read?.Data?.Id);
        Assert.Equal(100m, read?.Data?.TotalAmount);
        Assert.Equal(HttpStatusCode.BadRequest, individualChange.StatusCode);
        Assert.Contains(TransactionMessages.InstallmentFieldsRestricted,
            await individualChange.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.InstallmentPlans
            .Include(item => item.Installments)
            .SingleAsync(item => item.PublicId == plan.Id);
        Assert.Equal(3, stored.Installments.Count);
        Assert.Equal(100m, stored.Installments.Sum(item => item.Amount));
    }

    [FunctionalFact]
    public async Task GivenForeignPurchase_WhenRecorded_ThenEveryPartRetainsConversionEvidence()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Foreign card", "BRL");
        var category = await SeedCategoryAsync(subject, "Travel");
        await SeedRateAsync("USD", "BRL", 5m, Today.AddDays(-1));

        var response = await client.PostAsJsonAsync("/api/installment-plans", new
        {
            CreditCardId = card,
            CategoryId = category,
            TotalAmount = 10m,
            InstallmentCount = 3,
            PurchasedOn = Today,
            CurrencyCode = "USD",
            Counterparty = "Foreign shop"
        });
        var plan = (await response.Content.ReadFromJsonAsync<PlanEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(50m, plan.TotalAmount);
        Assert.Equal(10m, plan.OriginalTotalAmount);
        Assert.Equal("USD", plan.OriginalCurrencyCode);
        Assert.Equal(5m, plan.AppliedRate);
        Assert.All(plan.Installments, item =>
        {
            Assert.Equal("USD", item.OriginalCurrencyCode);
            Assert.Equal(5m, item.AppliedRate);
            Assert.Equal(Today.AddDays(-1), item.RateDate);
        });
        Assert.Equal(10m, plan.Installments.Sum(item => item.OriginalAmount));
    }

    [FunctionalFact]
    public async Task GivenAnInstallment_WhenDeletedDirectly_ThenWholePlanDeletesAndRestores()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Lifecycle card", "BRL");
        var category = await SeedCategoryAsync(subject, "Lifecycle");
        var plan = await RecordAsync(client, card, category, 90m, 3);

        var deleted = await client.DeleteAsync(
            $"/api/transactions/{plan.Installments.ElementAt(1).TransactionId}");
        var hidden = await client.GetAsync($"/api/installment-plans/{plan.Id}");
        var tombstone = await client.GetFromJsonAsync<PlanEnvelope>(
            $"/api/installment-plans/{plan.Id}?includeDeleted=true");
        var restored = await client.PostAsync(
            $"/api/installment-plans/{plan.Id}/restore",
            null);
        var live = await client.GetFromJsonAsync<PlanEnvelope>(
            $"/api/installment-plans/{plan.Id}");
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreignRead = await other.GetAsync($"/api/installment-plans/{plan.Id}");
        var foreignDelete = await other.DeleteAsync($"/api/installment-plans/{plan.Id}");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(tombstone?.Data?.IsDeleted);
        Assert.All(tombstone!.Data!.Installments, item => Assert.True(item.IsDeleted));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False(live?.Data?.IsDeleted);
        Assert.All(live!.Data!.Installments, item => Assert.False(item.IsDeleted));
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenSettledIntendedCycle_WhenRecorded_ThenLatePartUsesNextOpenCycle()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Late card", "BRL");
        var category = await SeedCategoryAsync(subject, "Late purchase");
        var charge = await RecordChargeAsync(client, card, category, 10m);
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        var account = await CreateAccountAsync(client, "Card payer");
        (await client.PostAsJsonAsync("/api/transfers", new
        {
            OriginFinancialAccountId = account,
            DestinationStatementId = charge.StatementId,
            Amount = 10m,
            OccurredOn = Today
        })).EnsureSuccessStatusCode();

        var plan = await RecordAsync(client, card, category, 60m, 3);

        Assert.True(plan.Installments.ElementAt(0).IsLateArriving);
        Assert.Equal(
            plan.Installments.ElementAt(0).StatementId,
            plan.Installments.ElementAt(1).StatementId);
        Assert.False(plan.Installments.ElementAt(1).IsLateArriving);
        Assert.NotEqual(
            plan.Installments.ElementAt(1).StatementId,
            plan.Installments.ElementAt(2).StatementId);
    }

    [FunctionalFact]
    public async Task GivenInvalidForeignOrUnauthorizedPurchase_WhenPosted_ThenNothingIsCreated()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Validation card", "BRL");
        var category = await SeedCategoryAsync(subject, "Validation");

        var invalid = await client.PostAsJsonAsync("/api/installment-plans", new
        {
            CreditCardId = card,
            CategoryId = category,
            TotalAmount = 0m,
            InstallmentCount = 1,
            PurchasedOn = default(DateOnly)
        });
        var noRate = await client.PostAsJsonAsync("/api/installment-plans", new
        {
            CreditCardId = card,
            CategoryId = category,
            TotalAmount = 10m,
            InstallmentCount = 2,
            PurchasedOn = Today,
            CurrencyCode = "USD"
        });
        using var anonymous = factory.CreateClient();
        var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/installment-plans",
            Request(card, category, 10m, 2));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(InstallmentPlanMessages.InstallmentCountMinimum,
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, noRate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await using var context = CreateContext();
        Assert.Empty(await context.InstallmentPlans.ToArrayAsync());
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private static object Request(
        Guid card,
        Guid category,
        decimal total,
        short count) => new
        {
            CreditCardId = card,
            CategoryId = category,
            TotalAmount = total,
            InstallmentCount = count,
            PurchasedOn = Today
        };

    private static async Task<PlanData> RecordAsync(
        HttpClient client,
        Guid card,
        Guid category,
        decimal total,
        short count)
    {
        var response = await client.PostAsJsonAsync(
            "/api/installment-plans",
            Request(card, category, total, count));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanEnvelope>())!.Data!;
    }

    private static async Task<Guid> CreateCardAsync(
        HttpClient client,
        string name,
        string currencyCode)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = currencyCode,
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 25,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = "Example Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = 1000m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<ChargeData> RecordChargeAsync(
        HttpClient client,
        Guid card,
        Guid category,
        decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            CreditCardId = card,
            CategoryId = category,
            Direction = TransactionDirection.Expense,
            Amount = amount,
            OccurredOn = Today
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChargeEnvelope>())!.Data!;
    }

    private async Task<Guid> SeedCategoryAsync(Guid subject, string name)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var category = new Category(user, name, DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.PublicId;
    }

    private async Task SeedRateAsync(
        string baseCode,
        string quoteCode,
        decimal rate,
        DateOnly rateDate)
    {
        await using var context = CreateContext();
        var baseCurrency = await context.Currencies.SingleAsync(item => item.Code == baseCode);
        var quoteCurrency = await context.Currencies.SingleAsync(item => item.Code == quoteCode);
        context.ExchangeRates.Add(new ExchangeRate(
            baseCurrency.Id,
            quoteCurrency.Id,
            rate,
            rateDate,
            ExchangeRateSource.Manual));
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

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Installment Owner"
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

    private sealed record PlanEnvelope(PlanData? Data);
    private sealed record PlanData(
        Guid Id,
        decimal TotalAmount,
        decimal? OriginalTotalAmount,
        string? OriginalCurrencyCode,
        decimal? AppliedRate,
        bool IsDeleted,
        IReadOnlyCollection<InstallmentData> Installments);
    private sealed record InstallmentData(
        Guid TransactionId,
        short Number,
        decimal Amount,
        decimal? OriginalAmount,
        string? OriginalCurrencyCode,
        decimal? AppliedRate,
        DateOnly? RateDate,
        DateOnly OccurredOn,
        Guid? StatementId,
        bool IsLateArriving,
        bool IsDeleted);
    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
    private sealed record ChargeEnvelope(ChargeData? Data);
    private sealed record ChargeData(Guid StatementId);
}
