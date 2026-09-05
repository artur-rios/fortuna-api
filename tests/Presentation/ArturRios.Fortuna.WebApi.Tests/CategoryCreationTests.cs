using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Security;
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

public sealed class CategoryCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenValidRootAndChild_WhenCreated_ThenOwnedHierarchyAndAuditsAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var rootResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "  Living  " });
        var root = (await rootResponse.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;
        var childResponse = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "  Dining  ", ParentId = root.Id });
        var child = (await childResponse.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, rootResponse.StatusCode);
        Assert.Null(root.ParentId);
        Assert.Equal("Living", root.Name);
        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal("Dining", child.Name);
        Assert.Equal(child.CreatedAt, child.UpdatedAt);
        await using var context = CreateContext();
        var stored = await context.Categories
            .Include(category => category.User)
            .SingleAsync(category => category.PublicId == child.Id);
        Assert.Equal(subject.ToString("D"), stored.User.ExternalSubject);
        Assert.Equal("DINING", stored.NormalizedName);
        Assert.Equal(root.Id, await context.Categories
            .Where(category => category.Id == stored.ParentId)
            .Select(category => category.PublicId)
            .SingleAsync());
        var audits = await context.AuditEntries
            .Where(entry => entry.Operation == "CreateCategoryCommand")
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal(AuditOutcome.Succeeded, audit.Outcome));
        Assert.Contains(audits, audit =>
            audit.EntityType == "Category" && audit.EntityPublicId == child.Id);
    }

    [FunctionalFact]
    public async Task GivenDuplicateSiblingName_WhenCreated_ThenConflictOnlyWithinThatParent()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var firstParent = await CreateAsync(client, "Home");
        var secondParent = await CreateAsync(client, "Leisure");
        var first = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Dining", ParentId = firstParent.Id });

        var duplicate = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "  dining  ", ParentId = firstParent.Id });
        var otherParent = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "dining", ParentId = secondParent.Id });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            CategoryMessages.DuplicateSiblingName,
            await duplicate.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, otherParent.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(2, await context.Categories.CountAsync(category =>
            category.NormalizedName == "DINING"));
    }

    [FunctionalFact]
    public async Task GivenParentFromAnotherOwner_WhenCreated_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(otherClient, Guid.NewGuid(), HeimdallRoles.User);
        var foreignParent = await CreateAsync(ownerClient, "Foreign");

        var response = await otherClient.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Hidden Child", ParentId = foreignParent.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            CategoryMessages.ParentNotFound,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Categories.AnyAsync(category =>
            category.Name == "Hidden Child"));
    }

    [FunctionalFact]
    public async Task GivenSoftDeletedParent_WhenCreated_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var parent = await CreateAsync(client, "Archived");
        await using (var context = CreateContext())
        {
            var category = await context.Categories.SingleAsync(item => item.PublicId == parent.Id);
            category.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Child", ParentId = parent.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var assertionContext = CreateContext();
        Assert.False(await assertionContext.Categories.AnyAsync(category =>
            category.Name == "Child"));
    }

    [FunctionalFact]
    public async Task GivenCyclicParentChain_WhenCreated_ThenBadRequestIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var root = await CreateAsync(client, "Root");
        var child = await CreateAsync(client, "Child", root.Id);
        await using (var context = CreateContext())
        {
            var rootCategory = await context.Categories.SingleAsync(item => item.PublicId == root.Id);
            var childId = await context.Categories
                .Where(item => item.PublicId == child.Id)
                .Select(item => item.Id)
                .SingleAsync();
            context.Entry(rootCategory).Property(item => item.ParentId).CurrentValue = childId;
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Rejected", ParentId = root.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            CategoryMessages.CycleDetected,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var assertionContext = CreateContext();
        Assert.False(await assertionContext.Categories.AnyAsync(category =>
            category.Name == "Rejected"));
    }

    [FunctionalFact]
    public async Task GivenConcurrentDuplicateNames_WhenCreated_ThenExactlyOneRequestWins()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var provisioningClient = factory.CreateClient();
        Authorize(provisioningClient, subject, HeimdallRoles.User);
        Assert.Equal(HttpStatusCode.OK, (await provisioningClient.GetAsync("/api/me")).StatusCode);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, subject, HeimdallRoles.User);
        Authorize(secondClient, subject, HeimdallRoles.User);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/categories", new { Name = "Concurrent" }),
            secondClient.PostAsJsonAsync("/api/categories", new { Name = " concurrent " }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        await using var context = CreateContext();
        Assert.Equal(1, await context.Categories.CountAsync(category =>
            category.NormalizedName == "CONCURRENT"));
    }

    [FunctionalFact]
    public async Task GivenInvalidFields_WhenCreated_ThenBadRequestNamesEveryFailure()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "", ParentId = Guid.Empty });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CategoryMessages.NameRequired, body, StringComparison.Ordinal);
        Assert.Contains(CategoryMessages.ParentIdInvalid, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Categories.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenCreated_ThenUnauthorizedStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Hidden" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Categories.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenCreated_ThenForbiddenStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = "Admin Category" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Categories.AnyAsync());
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

    private sealed record CategoryData(
        Guid Id,
        string Name,
        Guid? ParentId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
