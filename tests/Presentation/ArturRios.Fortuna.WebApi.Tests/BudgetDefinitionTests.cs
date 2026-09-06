using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Planning;
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

public sealed class BudgetDefinitionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenHierarchyAndOverlap_WhenBudgetsCreated_ThenConsumptionHonorsOptions()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateCategoryAsync(client, "Living");
        var child = await CreateCategoryAsync(client, "Dining", root);
        var accountId = await CreateAccountAsync(client);
        await CreateTransactionAsync(client, accountId, root, 20m, "Home expense");
        await CreateTransactionAsync(client, accountId, child, 30m, "Meal expense");

        var inclusiveResponse = await CreateBudgetAsync(client, 40m, [root]);
        var inclusive = (await inclusiveResponse.Content
            .ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        var exclusiveResponse = await CreateBudgetAsync(
            client, 100m, [root], includeDescendants: false);
        var exclusive = (await exclusiveResponse.Content
            .ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        var overlappingResponse = await CreateBudgetAsync(client, 200m, [root, child]);

        Assert.Equal(HttpStatusCode.Created, inclusiveResponse.StatusCode);
        Assert.Equal(50m, inclusive.CurrentPeriod.Spent);
        Assert.Equal(0m, inclusive.CurrentPeriod.Remaining);
        Assert.True(inclusive.CurrentPeriod.IsExceeded);
        Assert.Equal(10m, inclusive.CurrentPeriod.Overage);
        Assert.True(inclusive.IncludeDescendants);
        Assert.Equal(HttpStatusCode.Created, exclusiveResponse.StatusCode);
        Assert.Equal(20m, exclusive.CurrentPeriod.Spent);
        Assert.Equal(80m, exclusive.CurrentPeriod.Remaining);
        Assert.False(exclusive.CurrentPeriod.IsExceeded);
        Assert.False(exclusive.IncludeDescendants);
        Assert.Equal(HttpStatusCode.Created, overlappingResponse.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenBudget_WhenManaged_ThenCrudAndSoftDeletionAreApplied()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Groceries");
        var createResponse = await CreateBudgetAsync(client, 500m, [categoryId]);
        var created = (await createResponse.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var getResponse = await client.GetAsync($"/api/budgets/{created.Id}");
        var updateResponse = await client.PutAsJsonAsync($"/api/budgets/{created.Id}", new
        {
            Amount = 750m,
            CurrencyCode = "BRL",
            PeriodType = BudgetPeriodType.Quarterly,
            PeriodStart = new DateOnly(2026, 7, 1),
            CategoryIds = new[] { categoryId },
            IncludeDescendants = false
        });
        var updated = (await updateResponse.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        var list = (await client.GetFromJsonAsync<BudgetListEnvelope>("/api/budgets"))!.Data!;
        var deleteResponse = await client.DeleteAsync($"/api/budgets/{created.Id}");
        var hiddenResponse = await client.GetAsync($"/api/budgets/{created.Id}");
        var deletedResponse = await client.GetAsync(
            $"/api/budgets/{created.Id}?includeDeleted=true");
        var deleted = (await deletedResponse.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(750m, updated.Amount);
        Assert.Equal(BudgetPeriodType.Quarterly, updated.PeriodType);
        Assert.False(updated.IncludeDescendants);
        Assert.Equal(created.Id, Assert.Single(list.Budgets).Id);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deletedResponse.StatusCode);
        Assert.True(deleted.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrForeignCategories_WhenBudgetCreated_ThenRequestIsRejected()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreignCategory = await CreateCategoryAsync(other, "Foreign");

        var zero = await CreateBudgetAsync(owner, 0m, [Guid.NewGuid()]);
        var empty = await CreateBudgetAsync(owner, 100m, []);
        var foreign = await CreateBudgetAsync(owner, 100m, [foreignCategory]);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Contains(
            BudgetMessages.AmountMustBePositive,
            await zero.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains(
            BudgetMessages.CategoriesRequired,
            await empty.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Contains(
            BudgetMessages.CategoryNotFound,
            await foreign.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Budgets.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignOrUnauthorizedBudget_WhenAccessed_ThenItIsHiddenOrForbidden()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(owner, "Private");
        var response = await CreateBudgetAsync(owner, 100m, [categoryId]);
        var budget = (await response.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var foreignGet = await other.GetAsync($"/api/budgets/{budget.Id}");
        var foreignDelete = await other.DeleteAsync($"/api/budgets/{budget.Id}");
        using var anonymous = factory.CreateClient();
        var anonymousList = await anonymous.GetAsync("/api/budgets");
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var administratorList = await administrator.GetAsync("/api/budgets");

        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorList.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenPastPeriods_WhenConsumptionRequested_ThenEmptyAndOverageAreReported()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Past spending");
        var accountId = await CreateAccountAsync(client);
        await CreateTransactionAsync(
            client,
            accountId,
            categoryId,
            60m,
            "August expense",
            new DateOnly(2026, 8, 10));
        var create = await CreateBudgetAsync(
            client,
            50m,
            [categoryId],
            periodStart: new DateOnly(2026, 7, 1));
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var july = await GetConsumptionAsync(client, budget.Id, new DateOnly(2026, 7, 1));
        var august = await GetConsumptionAsync(client, budget.Id, new DateOnly(2026, 8, 1));

        Assert.Equal(0m, july.Spent);
        Assert.Equal(50m, july.Remaining);
        Assert.False(july.IsExceeded);
        Assert.Equal(60m, august.Spent);
        Assert.Equal(0m, august.Remaining);
        Assert.True(august.IsExceeded);
        Assert.Equal(10m, august.Overage);
    }

    [FunctionalFact]
    public async Task GivenSeveralCurrencies_WhenConsumptionRequested_ThenRatesAreReported()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Travel");
        var brlAccount = await CreateAccountAsync(client);
        var usdAccount = await CreateAccountAsync(client, "USD");
        await CreateTransactionAsync(
            client, brlAccount, categoryId, 10m, "Local", new DateOnly(2026, 8, 10));
        await CreateTransactionAsync(
            client, usdAccount, categoryId, 20m, "Foreign", new DateOnly(2026, 8, 10));
        await SeedRateAsync("USD", "BRL", 5m, new DateOnly(2026, 8, 9));
        var create = await CreateBudgetAsync(
            client,
            200m,
            [categoryId],
            periodStart: new DateOnly(2026, 8, 1));
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var consumption = await GetConsumptionAsync(
            client,
            budget.Id,
            new DateOnly(2026, 8, 1));

        Assert.Equal(110m, consumption.Spent);
        Assert.True(consumption.IsFullyConverted);
        Assert.Collection(
            consumption.Conversions.OrderBy(item => item.SourceCurrencyCode),
            brl =>
            {
                Assert.Equal("BRL", brl.SourceCurrencyCode);
                Assert.Equal(10m, brl.ConvertedAmount);
                Assert.Null(brl.AppliedRate);
            },
            usd =>
            {
                Assert.Equal("USD", usd.SourceCurrencyCode);
                Assert.Equal(20m, usd.SourceAmount);
                Assert.Equal(100m, usd.ConvertedAmount);
                Assert.Equal(5m, usd.AppliedRate);
                Assert.Equal(new DateOnly(2026, 8, 9), usd.RateDate);
                Assert.Equal(ExchangeRateSource.Manual, usd.RateSource);
            });
    }

    [FunctionalFact]
    public async Task GivenUnavailableRate_WhenConsumed_ThenPartialResultExplainsMissingRate()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Swiss expense");
        var accountId = await CreateAccountAsync(client, "CHF");
        await CreateTransactionAsync(
            client,
            accountId,
            categoryId,
            20m,
            "Unconverted",
            new DateOnly(2026, 8, 10));
        var create = await CreateBudgetAsync(
            client,
            200m,
            [categoryId],
            periodStart: new DateOnly(2026, 8, 1));
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var response = await client.GetAsync(
            $"/api/budgets/{budget.Id}/consumption?periodStart=2026-08-01");
        var consumption = (await response.Content
            .ReadFromJsonAsync<BudgetConsumptionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(consumption.IsFullyConverted);
        Assert.Null(consumption.Spent);
        var conversion = Assert.Single(consumption.Conversions);
        Assert.Equal("CHF", conversion.SourceCurrencyCode);
        Assert.Null(conversion.ConvertedAmount);
        Assert.Equal(FigureConversionMessages.RateUnavailable, conversion.UnconvertedReason);
        Assert.Contains(
            FigureConversionMessages.PartiallyConverted,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDateBeforeBudget_WhenConsumptionRequested_ThenReasonedEmptyResultReturns()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Future budget");
        var create = await CreateBudgetAsync(client, 100m, [categoryId]);
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var response = await client.GetAsync(
            $"/api/budgets/{budget.Id}/consumption?periodStart=2026-08-01");
        var consumption = (await response.Content
            .ReadFromJsonAsync<BudgetConsumptionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(consumption.IsCovered);
        Assert.Null(consumption.PeriodStart);
        Assert.Null(consumption.Spent);
        Assert.Empty(consumption.Conversions);
        Assert.Equal(BudgetMessages.PeriodPrecedesBudget, consumption.Reason);
    }

    [FunctionalFact]
    public async Task GivenTransferAndDeletedExpense_WhenConsumed_ThenBothAreExcluded()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Deleted expense");
        var origin = await CreateAccountAsync(client);
        var destination = await CreateAccountAsync(client);
        var transactionId = await CreateTransactionAsync(
            client, origin, categoryId, 25m, "Deleted", new DateOnly(2026, 9, 5));
        (await client.DeleteAsync($"/api/transactions/{transactionId}"))
            .EnsureSuccessStatusCode();
        var transferResponse = await client.PostAsJsonAsync("/api/transfers", new
        {
            OriginFinancialAccountId = origin,
            DestinationFinancialAccountId = destination,
            Amount = 30m,
            OccurredOn = new DateOnly(2026, 9, 5)
        });
        transferResponse.EnsureSuccessStatusCode();
        var transferId = (await transferResponse.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
        Guid transferCategoryId;
        await using (var context = CreateContext())
        {
            transferCategoryId = await context.Transfers
                .Where(item => item.PublicId == transferId)
                .Select(item => item.OutboundTransaction.Category.PublicId)
                .SingleAsync();
        }

        var create = await CreateBudgetAsync(
            client, 100m, [categoryId, transferCategoryId]);
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        var consumption = await GetConsumptionAsync(
            client,
            budget.Id,
            new DateOnly(2026, 9, 1));

        Assert.Equal(0m, consumption.Spent);
        Assert.Equal(100m, consumption.Remaining);
    }

    [FunctionalFact]
    public async Task GivenInstallmentPlan_WhenPeriodConsumed_ThenOnlyThatInstallmentCounts()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(client, "Installments");
        var cardId = await CreateCardAsync(client);
        var planResponse = await client.PostAsJsonAsync("/api/installment-plans", new
        {
            CreditCardId = cardId,
            CategoryId = categoryId,
            TotalAmount = 90m,
            InstallmentCount = 3,
            PurchasedOn = new DateOnly(2026, 8, 6)
        });
        planResponse.EnsureSuccessStatusCode();
        var create = await CreateBudgetAsync(
            client,
            100m,
            [categoryId],
            periodStart: new DateOnly(2026, 8, 1));
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;

        var consumption = await GetConsumptionAsync(
            client,
            budget.Id,
            new DateOnly(2026, 9, 1));

        Assert.Equal(30m, consumption.Spent);
        Assert.Equal(70m, consumption.Remaining);
    }

    [FunctionalFact]
    public async Task GivenForeignOrUnauthorizedConsumption_WhenRequested_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var categoryId = await CreateCategoryAsync(owner, "Private consumption");
        var create = await CreateBudgetAsync(owner, 100m, [categoryId]);
        var budget = (await create.Content.ReadFromJsonAsync<BudgetEnvelope>())!.Data!;
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreign = await other.GetAsync($"/api/budgets/{budget.Id}/consumption");
        var unauthorized = await anonymous.GetAsync($"/api/budgets/{budget.Id}/consumption");
        var forbidden = await administrator.GetAsync($"/api/budgets/{budget.Id}/consumption");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
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
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
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

    private static Task<HttpResponseMessage> CreateBudgetAsync(
        HttpClient client,
        decimal amount,
        IReadOnlyCollection<Guid> categoryIds,
        bool includeDescendants = true,
        DateOnly? periodStart = null) => client.PostAsJsonAsync("/api/budgets", new
        {
            Amount = amount,
            CurrencyCode = "BRL",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = periodStart ?? new DateOnly(2026, 9, 1),
            CategoryIds = categoryIds,
            IncludeDescendants = includeDescendants
        });

    private static async Task<Guid> CreateCategoryAsync(
        HttpClient client,
        string name,
        Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new
        {
            Name = $"{name} {Guid.NewGuid():N}",
            ParentId = parentId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string currencyCode = "BRL")
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = $"Account {Guid.NewGuid():N}",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = currencyCode,
            OpeningBalance = 1000m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateTransactionAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        string description,
        DateOnly? occurredOn = null)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = occurredOn ?? new DateOnly(2026, 9, 6),
            Amount = amount,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Counterparty = "Budget merchant",
            Description = description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = $"Card {Guid.NewGuid():N}",
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 25,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
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

    private static async Task<BudgetConsumptionDetailData> GetConsumptionAsync(
        HttpClient client,
        Guid budgetId,
        DateOnly periodStart)
    {
        var response = await client.GetAsync(
            $"/api/budgets/{budgetId}/consumption?periodStart={periodStart:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        return (await response.Content
            .ReadFromJsonAsync<BudgetConsumptionEnvelope>())!.Data!;
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
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

    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
    private sealed record BudgetEnvelope(BudgetData? Data);
    private sealed record BudgetConsumptionEnvelope(BudgetConsumptionDetailData? Data);
    private sealed record BudgetListEnvelope(BudgetListData? Data);
    private sealed record BudgetListData(IReadOnlyList<BudgetData> Budgets);
    private sealed record BudgetData(
        Guid Id,
        decimal Amount,
        string CurrencyCode,
        BudgetPeriodType PeriodType,
        DateOnly PeriodStart,
        bool IncludeDescendants,
        IReadOnlyList<BudgetCategoryData> Categories,
        BudgetConsumptionData CurrentPeriod,
        bool IsDeleted);
    private sealed record BudgetCategoryData(Guid Id, string Name);
    private sealed record BudgetConsumptionData(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        decimal? Spent,
        decimal? Remaining,
        bool? IsExceeded,
        decimal? Overage,
        bool IsFullyConverted);
    private sealed record BudgetConsumptionDetailData(
        Guid BudgetId,
        decimal BudgetAmount,
        string CurrencyCode,
        DateOnly RequestedDate,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        decimal? Spent,
        decimal? Remaining,
        bool? IsExceeded,
        decimal? Overage,
        bool IsCovered,
        bool IsFullyConverted,
        string? Reason,
        IReadOnlyList<BudgetConversionData> Conversions);
    private sealed record BudgetConversionData(
        string SourceCurrencyCode,
        decimal SourceAmount,
        decimal? ConvertedAmount,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
