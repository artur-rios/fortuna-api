using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
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
        bool includeDescendants = true) => client.PostAsJsonAsync("/api/budgets", new
        {
            Amount = amount,
            CurrencyCode = "BRL",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = new DateOnly(2026, 9, 1),
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

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = $"Account {Guid.NewGuid():N}",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = 0m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task CreateTransactionAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        string description)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = new DateOnly(2026, 9, 6),
            Amount = amount,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Counterparty = "Budget merchant",
            Description = description
        });
        response.EnsureSuccessStatusCode();
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
