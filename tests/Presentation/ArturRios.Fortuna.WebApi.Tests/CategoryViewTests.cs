using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
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

public sealed class CategoryViewTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly Today = new(2026, 9, 5);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedHierarchy_WhenTreeRequested_ThenNestedSortedNodesAreReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateCategoryAsync(client, "Living");
        var second = await CreateCategoryAsync(client, "Automotive");
        var child = await CreateCategoryAsync(client, "Dining", root.Id);

        var response = await client.GetAsync("/api/categories");
        var tree = (await response.Content.ReadFromJsonAsync<TreeEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([second.Id, root.Id], tree.Categories.Select(category => category.Id));
        var nested = Assert.Single(tree.Categories[1].Children);
        Assert.Equal(child.Id, nested.Id);
        Assert.Equal(root.Id, nested.ParentId);
        Assert.Null(nested.UsageCount);
        Assert.False(tree.CanSeedDefaults);
    }

    [FunctionalFact]
    public async Task GivenNoCategories_WhenTreeRequested_ThenEmptyTreeOffersDefaults()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.GetAsync("/api/categories");
        var body = await response.Content.ReadAsStringAsync();
        var tree = (await response.Content.ReadFromJsonAsync<TreeEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(tree.Categories);
        Assert.True(tree.CanSeedDefaults);
        Assert.Contains(CategoryMessages.DefaultSetAvailable, body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedCategory_WhenTreeRequested_ThenItRequiresExplicitInclusion()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var live = await CreateCategoryAsync(client, "Live");
        var deleted = await CreateCategoryAsync(client, "Deleted");
        await SoftDeleteAsync(deleted.Id);

        var normal = (await client.GetFromJsonAsync<TreeEnvelope>("/api/categories"))!.Data!;
        var included = (await client.GetFromJsonAsync<TreeEnvelope>(
            "/api/categories?includeDeleted=true"))!.Data!;

        Assert.Equal(live.Id, Assert.Single(normal.Categories).Id);
        Assert.Equal(2, included.Categories.Count);
        Assert.True(included.Categories.Single(category => category.Id == deleted.Id).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenLiveTransactions_WhenUsageRequested_ThenCountsIncludeDescendantsOnly()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateCategoryAsync(client, "Living");
        var child = await CreateCategoryAsync(client, "Dining", root.Id);
        var grandchild = await CreateCategoryAsync(client, "Restaurants", child.Id);
        var accountId = await CreateAccountAsync(client);
        await RecordAsync(client, accountId, root.Id);
        await RecordAsync(client, accountId, child.Id);
        var deletedTransaction = await RecordAsync(client, accountId, child.Id);
        await RecordAsync(client, accountId, grandchild.Id);
        (await client.DeleteAsync($"/api/transactions/{deletedTransaction}"))
            .EnsureSuccessStatusCode();

        var tree = (await client.GetFromJsonAsync<TreeEnvelope>(
            "/api/categories?includeUsageCounts=true"))!.Data!;

        var rootOutput = Assert.Single(tree.Categories);
        Assert.Equal(3, rootOutput.UsageCount);
        var childOutput = Assert.Single(rootOutput.Children);
        Assert.Equal(2, childOutput.UsageCount);
        Assert.Equal(1, Assert.Single(childOutput.Children).UsageCount);
    }

    [FunctionalFact]
    public async Task GivenOwnedCategory_WhenRequestedById_ThenItsSubtreeIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateCategoryAsync(client, "Living");
        var child = await CreateCategoryAsync(client, "Dining", root.Id);

        var response = await client.GetAsync($"/api/categories/{root.Id}");
        var category = (await response.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(root.Id, category.Id);
        Assert.Equal(child.Id, Assert.Single(category.Children).Id);
    }

    [FunctionalFact]
    public async Task GivenForeignOrDeletedCategory_WhenRequestedById_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreign = await CreateCategoryAsync(owner, "Private");
        var deleted = await CreateCategoryAsync(other, "Archived");
        await SoftDeleteAsync(deleted.Id);

        var foreignResponse = await other.GetAsync($"/api/categories/{foreign.Id}");
        var deletedResponse = await other.GetAsync($"/api/categories/{deleted.Id}");
        var includedResponse = await other.GetAsync(
            $"/api/categories/{deleted.Id}?includeDeleted=true");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, includedResponse.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrWrongRole_WhenTreeRequested_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var unauthorized = await anonymous.GetAsync("/api/categories");
        var forbidden = await administrator.GetAsync("/api/categories");

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

    private async Task SoftDeleteAsync(Guid id)
    {
        await using var context = CreateContext();
        var category = await context.Categories.SingleAsync(item => item.PublicId == id);
        category.SoftDelete(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    private static async Task<CategoryData> CreateCategoryAsync(
        HttpClient client,
        string name,
        Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = name, ParentId = parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = "Category usage account",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = 0m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> RecordAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = Today,
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionEnvelope>())!.Data!.Id;
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

    private sealed record TreeEnvelope(TreeData? Data);
    private sealed record CategoryEnvelope(CategoryData? Data);
    private sealed record AccountEnvelope(AccountData? Data);
    private sealed record TransactionEnvelope(TransactionData? Data);
    private sealed record TreeData(List<CategoryData> Categories, bool CanSeedDefaults);
    private sealed record CategoryData(
        Guid Id,
        string Name,
        Guid? ParentId,
        bool IsDeleted,
        int? UsageCount,
        List<CategoryData> Children);
    private sealed record AccountData(Guid Id);
    private sealed record TransactionData(Guid Id);
}
