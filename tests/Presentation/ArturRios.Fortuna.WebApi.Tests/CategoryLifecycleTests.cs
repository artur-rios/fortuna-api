using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
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

public sealed class CategoryLifecycleTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenCategoryTree_WhenDeletedAndRestored_ThenOnlyCascadeIsReversed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateCategoryAsync(client, "Lifecycle root");
        var child = await CreateCategoryAsync(client, "Lifecycle child", root.Id);
        var grandchild = await CreateCategoryAsync(client, "Lifecycle grandchild", child.Id);
        var preDeleted = await CreateCategoryAsync(client, "Already deleted", root.Id);
        (await client.DeleteAsync($"/api/categories/{preDeleted.Id}"))
            .EnsureSuccessStatusCode();
        var rootTransaction = await AddTransactionAsync(root.Id, "Root history");
        var childTransaction = await AddTransactionAsync(child.Id, "Child history");

        var deleted = await client.DeleteAsync($"/api/categories/{root.Id}");
        var hidden = await client.GetAsync($"/api/categories/{root.Id}");
        var retained = await client.GetAsync(
            $"/api/categories/{root.Id}?includeDeleted=true");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        await using (var context = CreateContext())
        {
            var categories = await context.Categories
                .Where(item => new[] { root.Id, child.Id, grandchild.Id, preDeleted.Id }
                    .Contains(item.PublicId))
                .ToDictionaryAsync(item => item.PublicId);
            Assert.All(categories.Values, item => Assert.True(item.IsDeleted));
            Assert.Equal(
                categories[root.Id].DeletionCascadeId,
                categories[child.Id].DeletionCascadeId);
            Assert.Equal(
                categories[root.Id].DeletionCascadeId,
                categories[grandchild.Id].DeletionCascadeId);
            Assert.NotEqual(
                categories[root.Id].DeletionCascadeId,
                categories[preDeleted.Id].DeletionCascadeId);
            Assert.All(
                await context.FinancialTransactions
                    .Where(item => new[] { rootTransaction, childTransaction }
                        .Contains(item.PublicId))
                    .ToArrayAsync(),
                item => Assert.False(item.IsDeleted));
        }

        var restored = await client.PostAsync($"/api/categories/{root.Id}/restore", null);

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await using (var context = CreateContext())
        {
            var categories = await context.Categories
                .Where(item => new[] { root.Id, child.Id, grandchild.Id, preDeleted.Id }
                    .Contains(item.PublicId))
                .ToDictionaryAsync(item => item.PublicId);
            Assert.False(categories[root.Id].IsDeleted);
            Assert.False(categories[child.Id].IsDeleted);
            Assert.False(categories[grandchild.Id].IsDeleted);
            Assert.True(categories[preDeleted.Id].IsDeleted);
            var audits = await context.AuditEntries
                .Where(item =>
                    item.EntityPublicId == root.Id &&
                    (item.Operation == "DeleteCategoryCommand" ||
                        item.Operation == "RestoreCategoryCommand"))
                .ToArrayAsync();
            Assert.Equal(2, audits.Length);
            Assert.All(audits, item => Assert.Equal(AuditOutcome.Succeeded, item.Outcome));
        }
    }

    [FunctionalFact]
    public async Task GivenLiveCategory_WhenHardDeletedOrRestored_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var category = await CreateCategoryAsync(client, "Still live");

        var hardDelete = await client.DeleteAsync($"/api/categories/{category.Id}/hard");
        var restore = await client.PostAsync($"/api/categories/{category.Id}/restore", null);

        Assert.Equal(HttpStatusCode.Conflict, hardDelete.StatusCode);
        Assert.Contains(
            CategoryMessages.HardDeleteRequiresSoftDeletion,
            await hardDelete.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        Assert.Contains(
            CategoryMessages.RestoreRequiresSoftDeletion,
            await restore.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenLiveSubtreeTransactions_WhenHardDeleted_ThenCountRequiresReassignment()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Referenced tree");
        var child = await CreateCategoryAsync(client, "Referenced child", source.Id);
        var target = await CreateCategoryAsync(client, "Reassignment target");
        var direct = await AddTransactionAsync(source.Id, "Direct reference");
        var nested = await AddTransactionAsync(child.Id, "Nested reference");
        var archived = await AddTransactionAsync(source.Id, "Archived reference", deleted: true);
        (await client.DeleteAsync($"/api/categories/{source.Id}"))
            .EnsureSuccessStatusCode();

        var refused = await client.DeleteAsync($"/api/categories/{source.Id}/hard");
        var refusal = await refused.Content.ReadFromJsonAsync<LifecycleEnvelope>();

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(source.Id, refusal?.Data?.Id);
        Assert.Equal(2, refusal?.Data?.LiveTransactionCount);
        Assert.Contains(CategoryMessages.HardDeleteHasLiveTransactions, refusal!.Errors);

        (await client.PostAsync($"/api/categories/{source.Id}/restore", null))
            .EnsureSuccessStatusCode();
        var reassigned = await client.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = target.Id, IncludeDescendants = true });
        reassigned.EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/categories/{source.Id}"))
            .EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync($"/api/categories/{source.Id}/hard");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Categories.AnyAsync(item =>
            item.PublicId == source.Id || item.PublicId == child.Id));
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            item.PublicId == archived));
        Assert.All(
            await context.FinancialTransactions
                .Where(item => new[] { direct, nested }.Contains(item.PublicId))
                .Select(item => item.Category.PublicId)
                .ToArrayAsync(),
            categoryId => Assert.Equal(target.Id, categoryId));
    }

    [FunctionalFact]
    public async Task GivenDeletedDuplicate_WhenRestored_ThenConflictKeepsItDeleted()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var archived = await CreateCategoryAsync(client, "Duplicate");
        (await client.DeleteAsync($"/api/categories/{archived.Id}"))
            .EnsureSuccessStatusCode();
        _ = await CreateCategoryAsync(client, "Duplicate");

        var response = await client.PostAsync($"/api/categories/{archived.Id}/restore", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            CategoryMessages.DuplicateSiblingName,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True((await context.Categories.SingleAsync(item =>
            item.PublicId == archived.Id)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenForeignMissingOrUnauthorizedCategory_WhenLifecycleRuns_ThenAccessIsHidden()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var category = await CreateCategoryAsync(owner, "Private lifecycle");
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreignDelete = await other.DeleteAsync($"/api/categories/{category.Id}");
        var missingRestore = await other.PostAsync(
            $"/api/categories/{Guid.NewGuid()}/restore",
            null);
        var anonymousHardDelete = await anonymous.DeleteAsync(
            $"/api/categories/{category.Id}/hard");
        var administratorDelete = await administrator.DeleteAsync(
            $"/api/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRestore.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHardDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDelete.StatusCode);
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

    private async Task<Guid> AddTransactionAsync(
        Guid categoryId,
        string description,
        bool deleted = false)
    {
        await using var context = CreateContext();
        var category = await context.Categories
            .Include(item => item.User)
            .SingleAsync(item => item.PublicId == categoryId);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var account = new FinancialAccount(
            category.User,
            $"Account {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            0,
            DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            category.User,
            account,
            category,
            TransactionDirection.Expense,
            25,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow,
            description);
        if (deleted)
        {
            transaction.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.FinancialAccounts.Add(account);
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static async Task<CategoryData> CreateCategoryAsync(
        HttpClient client,
        string name,
        Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;
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

    private sealed record CategoryEnvelope(CategoryData? Data);
    private sealed record CategoryData(Guid Id);
    private sealed record LifecycleEnvelope(
        LifecycleData? Data,
        IReadOnlyCollection<string> Errors);
    private sealed record LifecycleData(Guid Id, int LiveTransactionCount);
}
