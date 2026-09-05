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

public sealed class CategoryUpdateTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenNewNameAndParent_WhenUpdated_ThenCategoryAndAuditChangeButTransactionDoesNot()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var oldParent = await CreateAsync(client, "Old Parent");
        var newParent = await CreateAsync(client, "New Parent");
        var category = await CreateAsync(client, "Before", oldParent.Id);
        var transactionId = await AddTransactionAsync(category.Id);

        var response = await client.PutAsJsonAsync($"/api/categories/{category.Id}", new
        {
            Name = "  After  ",
            ParentId = newParent.Id
        });
        var envelope = await response.Content.ReadFromJsonAsync<CategoryEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(category.Id, envelope?.Data?.Id);
        Assert.Equal("After", envelope?.Data?.Name);
        Assert.Equal(newParent.Id, envelope?.Data?.ParentId);
        Assert.True(envelope?.Data?.UpdatedAt >= category.UpdatedAt);
        Assert.Contains(CategoryMessages.UpdatedSuccessfully, envelope!.Messages);
        await using var context = CreateContext();
        var stored = await context.Categories
            .Include(item => item.Parent)
            .SingleAsync(item => item.PublicId == category.Id);
        Assert.Equal("After", stored.Name);
        Assert.Equal("AFTER", stored.NormalizedName);
        Assert.Equal(newParent.Id, stored.Parent!.PublicId);
        Assert.Equal(category.Id, await context.FinancialTransactions
            .Where(item => item.PublicId == transactionId)
            .Select(item => item.Category.PublicId)
            .SingleAsync());
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateCategoryCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("Category", audit.EntityType);
        Assert.Equal(category.Id, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenSelfOrDescendantParent_WhenUpdated_ThenCycleIsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateAsync(client, "Root");
        var child = await CreateAsync(client, "Child", root.Id);

        var self = await client.PutAsJsonAsync($"/api/categories/{root.Id}", new
        {
            Name = "Root",
            ParentId = root.Id
        });
        var descendant = await client.PutAsJsonAsync($"/api/categories/{root.Id}", new
        {
            Name = "Root",
            ParentId = child.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, descendant.StatusCode);
        Assert.Contains(CategoryMessages.CycleDetected, await self.Content.ReadAsStringAsync());
        Assert.Contains(CategoryMessages.CycleDetected, await descendant.Content.ReadAsStringAsync());
        await using var context = CreateContext();
        var stored = await context.Categories.SingleAsync(item => item.PublicId == root.Id);
        Assert.Null(stored.ParentId);
    }

    [FunctionalFact]
    public async Task GivenDuplicateNameAtDestination_WhenUpdated_ThenConflictLeavesCategoryUnchanged()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var sourceParent = await CreateAsync(client, "Source");
        var destinationParent = await CreateAsync(client, "Destination");
        var category = await CreateAsync(client, "Before", sourceParent.Id);
        await CreateAsync(client, "Dining", destinationParent.Id);

        var response = await client.PutAsJsonAsync($"/api/categories/{category.Id}", new
        {
            Name = " dining ",
            ParentId = destinationParent.Id
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            CategoryMessages.DuplicateSiblingName,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.Categories
            .Include(item => item.Parent)
            .SingleAsync(item => item.PublicId == category.Id);
        Assert.Equal("Before", stored.Name);
        Assert.Equal(sourceParent.Id, stored.Parent!.PublicId);
    }

    [FunctionalFact]
    public async Task GivenNullParent_WhenUpdated_ThenCategoryMovesToRoot()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var parent = await CreateAsync(client, "Parent");
        var category = await CreateAsync(client, "Child", parent.Id);

        var response = await client.PutAsJsonAsync($"/api/categories/{category.Id}", new
        {
            Name = "Root Category",
            ParentId = (Guid?)null
        });
        var envelope = await response.Content.ReadFromJsonAsync<CategoryEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(envelope?.Data?.ParentId);
        await using var context = CreateContext();
        Assert.Null((await context.Categories.SingleAsync(item =>
            item.PublicId == category.Id)).ParentId);
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignCategory_WhenUpdated_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var deleted = await CreateAsync(ownerClient, "Deleted");
        var foreign = await CreateAsync(ownerClient, "Foreign");
        await using (var context = CreateContext())
        {
            var stored = await context.Categories.SingleAsync(item => item.PublicId == deleted.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var otherClient = factory.CreateClient();
        Authorize(otherClient, Guid.NewGuid(), HeimdallRoles.User);

        var deletedResponse = await ownerClient.PutAsJsonAsync(
            $"/api/categories/{deleted.Id}",
            UpdateBody("Changed"));
        var foreignResponse = await otherClient.PutAsJsonAsync(
            $"/api/categories/{foreign.Id}",
            UpdateBody("Changed"));

        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains(CategoryMessages.NotFound, await deletedResponse.Content.ReadAsStringAsync());
        Assert.Contains(CategoryMessages.NotFound, await foreignResponse.Content.ReadAsStringAsync());
        await using var assertionContext = CreateContext();
        Assert.False(await assertionContext.Categories.AnyAsync(item => item.Name == "Changed"));
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignParent_WhenUpdated_ThenParentIsHidden()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var category = await CreateAsync(ownerClient, "Category");
        var deletedParent = await CreateAsync(ownerClient, "Deleted Parent");
        await using (var context = CreateContext())
        {
            var stored = await context.Categories.SingleAsync(item =>
                item.PublicId == deletedParent.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var foreignClient = factory.CreateClient();
        Authorize(foreignClient, Guid.NewGuid(), HeimdallRoles.User);
        var foreignParent = await CreateAsync(foreignClient, "Foreign Parent");

        var deletedResponse = await ownerClient.PutAsJsonAsync(
            $"/api/categories/{category.Id}",
            UpdateBody("Category", deletedParent.Id));
        var foreignResponse = await ownerClient.PutAsJsonAsync(
            $"/api/categories/{category.Id}",
            UpdateBody("Category", foreignParent.Id));

        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains(CategoryMessages.ParentNotFound, await deletedResponse.Content.ReadAsStringAsync());
        Assert.Contains(CategoryMessages.ParentNotFound, await foreignResponse.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenInvalidBodyOrAccess_WhenUpdated_ThenRequestIsRefused()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var category = await CreateAsync(client, "Category");
        var invalid = await client.PutAsJsonAsync($"/api/categories/{category.Id}", new
        {
            Name = string.Empty,
            ParentId = Guid.Empty
        });
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/categories/{category.Id}",
            UpdateBody("Changed"));
        var administratorResponse = await administrator.PutAsJsonAsync(
            $"/api/categories/{category.Id}",
            UpdateBody("Changed"));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(CategoryMessages.NameRequired, await invalid.Content.ReadAsStringAsync());
        Assert.Contains(CategoryMessages.ParentIdInvalid, await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
        await using var context = CreateContext();
        Assert.True(await context.Categories.AnyAsync(item =>
            item.PublicId == category.Id && item.Name == "Category"));
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

    private async Task<Guid> AddTransactionAsync(Guid categoryId)
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
            "Keeps classification");
        context.FinancialAccounts.Add(account);
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static async Task<CategoryData> CreateAsync(
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

    private static object UpdateBody(string name, Guid? parentId = null) => new
    {
        Name = name,
        ParentId = parentId
    };

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

    private sealed record CategoryEnvelope(
        CategoryData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record CategoryData(
        Guid Id,
        string Name,
        Guid? ParentId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
