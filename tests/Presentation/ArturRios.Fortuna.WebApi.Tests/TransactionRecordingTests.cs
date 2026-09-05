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

public sealed class TransactionRecordingTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedAccountAndCategory_WhenRecorded_ThenDetailsAndBalanceAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Daily", 100m);
        var categoryId = await SeedCategoryAsync(subject, "Dining");

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 25m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Description = "  Team lunch  ",
            Counterparty = "Corner Cafe",
            Tags = new[] { "Food", " food " }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<TransactionEnvelope>();
        Assert.Equal(25m, envelope!.Data!.Amount);
        Assert.Equal("BRL", envelope.Data.CurrencyCode);
        Assert.Equal("Team lunch", envelope.Data.Description);
        Assert.Equal("Corner Cafe", envelope.Data.CounterpartyName);
        Assert.Single(envelope.Data.Tags);
        var balance = await client.GetFromJsonAsync<BalanceEnvelope>(
            $"/api/accounts/{accountId}/balance?asOf={Today:yyyy-MM-dd}");
        Assert.Equal(75m, balance!.Data!.Balance);

        await using var context = CreateContext();
        var stored = await context.FinancialTransactions
            .Include(item => item.Category)
            .Include(item => item.Counterparty)
            .Include(item => item.Currency)
            .Include(item => item.Tags)
            .SingleAsync(item => item.PublicId == envelope.Data.Id);
        Assert.Equal(categoryId, stored.Category.PublicId);
        Assert.Equal(TransactionSourceType.Manual, stored.SourceType);
        Assert.False(stored.IsReconciled);
        Assert.Single(stored.Tags);
        Assert.Contains(await context.AuditEntries.ToArrayAsync(), item =>
            item.Operation == "RecordTransactionCommand" &&
            item.EntityPublicId == stored.PublicId);
    }

    [FunctionalFact]
    public async Task GivenExistingLabels_WhenRecordedAgain_ThenCounterpartyAndTagsAreReused()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Labels", 0m);
        var categoryId = await SeedCategoryAsync(subject, "Food");
        var first = await RecordAsync(
            client,
            accountId,
            categoryId,
            counterparty: "Market",
            tags: ["Groceries"]);

        var second = await RecordAsync(
            client,
            accountId,
            categoryId,
            counterparty: " market ",
            tags: [" groceries "]);

        Assert.Equal(first.CounterpartyId, second.CounterpartyId);
        Assert.Equal(first.Tags.Single().Id, second.Tags.Single().Id);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Counterparties.CountAsync());
        Assert.Equal(1, await context.Tags.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignCurrency_WhenRateExists_ThenConvertedAndOriginalAmountsAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Brazil", 0m);
        var categoryId = await SeedCategoryAsync(subject, "Travel");
        var rateDate = Today.AddDays(-1);
        await SeedRateAsync("USD", "BRL", 5.123m, rateDate);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 2.005m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            CurrencyCode = "usd"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<TransactionEnvelope>())!.Data!;
        Assert.Equal(10.27m, data.Amount);
        Assert.Equal("BRL", data.CurrencyCode);
        Assert.Equal(2.005m, data.OriginalAmount);
        Assert.Equal("USD", data.OriginalCurrencyCode);
        Assert.Equal(5.123m, data.AppliedRate);
        Assert.Equal(rateDate, data.RateDate);
    }

    [FunctionalFact]
    public async Task GivenUnavailableOrUnknownCurrency_WhenRecorded_ThenNothingIsCreated()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Rates", 0m);
        var categoryId = await SeedCategoryAsync(subject, "Travel");

        var unavailable = await client.PostAsJsonAsync("/api/transactions", Request(
            accountId,
            categoryId,
            currencyCode: "USD"));
        var unknown = await client.PostAsJsonAsync("/api/transactions", Request(
            accountId,
            categoryId,
            currencyCode: "ZZZ"));

        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        Assert.Contains(
            TransactionMessages.ExchangeRateUnavailable,
            await unavailable.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains(
            TransactionMessages.CurrencyNotSupported,
            await unknown.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Empty(await context.FinancialTransactions.ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignOrDeletedDependencies_WhenRecorded_ThenNotFoundIsReturned()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        var ownerAccount = await CreateAccountAsync(owner, "Owner", 0m);
        var deletedAccount = await CreateAccountAsync(owner, "Deleted", 0m);
        (await other.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var ownerCategory = await SeedCategoryAsync(ownerSubject, "Owner Category");
        var foreignCategory = await SeedCategoryAsync(otherSubject, "Foreign Category");
        (await owner.DeleteAsync($"/api/accounts/{deletedAccount}")).EnsureSuccessStatusCode();

        var foreignTarget = await other.PostAsJsonAsync(
            "/api/transactions",
            Request(ownerAccount, foreignCategory));
        var deletedTarget = await owner.PostAsJsonAsync(
            "/api/transactions",
            Request(deletedAccount, ownerCategory));
        var foreignClassification = await owner.PostAsJsonAsync(
            "/api/transactions",
            Request(ownerAccount, foreignCategory));

        Assert.Equal(HttpStatusCode.NotFound, foreignTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignClassification.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenInvalidCoreFields_WhenRecorded_ThenBadRequestIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Validation", 0m);
        var categoryId = await SeedCategoryAsync(subject, "General");

        var amount = await client.PostAsJsonAsync(
            "/api/transactions",
            Request(accountId, categoryId, amount: 0m));
        var future = await client.PostAsJsonAsync(
            "/api/transactions",
            Request(accountId, categoryId, occurredOn: Today.AddDays(2)));
        var both = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CreditCardId = Guid.NewGuid(),
            CategoryId = categoryId
        });
        var owner = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            OwnerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, amount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, owner.StatusCode);
        Assert.Contains(
            TransactionMessages.OwnerImmutable,
            await owner.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenRecorded_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var request = Request(Guid.NewGuid(), Guid.NewGuid());

        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/transactions", request);
        var administratorResponse = await administrator.PostAsJsonAsync(
            "/api/transactions",
            request);

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

    private async Task<TransactionData> RecordAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        string? counterparty = null,
        string[]? tags = null)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Counterparty = counterparty,
            Tags = tags
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionEnvelope>())!.Data!;
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

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!.Id;
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

    private static object Request(
        Guid accountId,
        Guid categoryId,
        decimal amount = 10m,
        DateOnly? occurredOn = null,
        string? currencyCode = null) => new
        {
            OccurredOn = occurredOn ?? Today,
            Amount = amount,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            CurrencyCode = currencyCode
        };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Transaction Owner"
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

    private sealed record AccountEnvelope(AccountData? Data);
    private sealed record AccountData(Guid Id);
    private sealed record BalanceEnvelope(BalanceData? Data);
    private sealed record BalanceData(decimal Balance);
    private sealed record TransactionEnvelope(
        TransactionData? Data,
        IReadOnlyCollection<string> Messages);
    private sealed record TransactionData(
        Guid Id,
        decimal Amount,
        string CurrencyCode,
        decimal? OriginalAmount,
        string? OriginalCurrencyCode,
        decimal? AppliedRate,
        DateOnly? RateDate,
        string? Description,
        Guid? CounterpartyId,
        string? CounterpartyName,
        IReadOnlyCollection<TransactionTagData> Tags);
    private sealed record TransactionTagData(Guid Id, string Name);
}
