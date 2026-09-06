using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Classification;
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

public sealed class TagManagementTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedTags_WhenCreatedListedAndUpdated_ThenSortedSetIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var zeta = await CreateTagAsync(client, " Zeta ");
        var alpha = await CreateTagAsync(client, "Alpha");

        var before = (await client.GetFromJsonAsync<TagListEnvelope>("/api/tags"))!.Data!;
        var update = await client.PutAsJsonAsync(
            $"/api/tags/{zeta.Id}",
            new { Name = "Beta" });
        var updated = (await update.Content.ReadFromJsonAsync<TagEnvelope>())!.Data!;
        var after = (await client.GetFromJsonAsync<TagListEnvelope>("/api/tags"))!.Data!;

        Assert.Equal([alpha.Id, zeta.Id], before.Tags.Select(item => item.Id));
        Assert.Equal("Zeta", before.Tags[1].Name);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Beta", updated.Name);
        Assert.Equal(["Alpha", "Beta"], after.Tags.Select(item => item.Name));
    }

    [FunctionalFact]
    public async Task GivenDuplicateOrInvalidName_WhenMutated_ThenRequestIsRefused()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var first = await CreateTagAsync(client, "Food");
        var second = await CreateTagAsync(client, "Travel");

        var duplicateCreate = await client.PostAsJsonAsync("/api/tags", new { Name = " food " });
        var duplicateUpdate = await client.PutAsJsonAsync(
            $"/api/tags/{second.Id}",
            new { Name = "FOOD" });
        var invalid = await client.PostAsJsonAsync("/api/tags", new { Name = "" });

        Assert.Equal(HttpStatusCode.Conflict, duplicateCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(
            TagMessages.DuplicateName,
            await duplicateCreate.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains(
            TagMessages.NameRequired,
            await invalid.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.NotEqual(first.Id, second.Id);
    }

    [FunctionalFact]
    public async Task GivenLiveTagAndTransaction_WhenAttachedAndDetached_ThenRequestsAreIdempotent()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var tag = await CreateTagAsync(client, "Food");
        var transactionId = await CreateTransactionAsync(client, "Tagged expense");

        var firstAttach = await client.PostAsync(
            $"/api/transactions/{transactionId}/tags/{tag.Id}",
            null);
        var secondAttach = await client.PostAsync(
            $"/api/transactions/{transactionId}/tags/{tag.Id}",
            null);
        var attached = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{transactionId}");
        var firstDetach = await client.DeleteAsync(
            $"/api/transactions/{transactionId}/tags/{tag.Id}");
        var secondDetach = await client.DeleteAsync(
            $"/api/transactions/{transactionId}/tags/{tag.Id}");

        Assert.Equal(HttpStatusCode.OK, firstAttach.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondAttach.StatusCode);
        Assert.Equal(tag.Id, Assert.Single(attached!.Data!.Tags).Id);
        Assert.Contains(
            TagMessages.AlreadyAttached,
            await secondAttach.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, firstDetach.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondDetach.StatusCode);
        Assert.Contains(
            TagMessages.AlreadyDetached,
            await secondDetach.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var assignments = await context.AuditEntries.CountAsync(item =>
            item.EntityPublicId == transactionId &&
            (item.Operation == "AttachTransactionTagCommand" ||
                item.Operation == "DetachTransactionTagCommand") &&
            item.Outcome == AuditOutcome.Succeeded);
        Assert.Equal(4, assignments);
    }

    [FunctionalFact]
    public async Task GivenAttachedTag_WhenDeleted_ThenAttachmentsAreRemovedAndCounted()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var tag = await CreateTagAsync(client, "Temporary");
        var first = await CreateTransactionAsync(client, "First tagged expense");
        var second = await CreateTransactionAsync(client, "Second tagged expense");
        (await client.PostAsync($"/api/transactions/{first}/tags/{tag.Id}", null))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/transactions/{second}/tags/{tag.Id}", null))
            .EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/tags/{tag.Id}");
        var deletion = (await response.Content.ReadFromJsonAsync<TagEnvelope>())!.Data!;
        var live = (await client.GetFromJsonAsync<TagListEnvelope>("/api/tags"))!.Data!;
        var all = (await client.GetFromJsonAsync<TagListEnvelope>(
            "/api/tags?includeDeleted=true"))!.Data!;
        var firstTransaction = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{first}");
        var secondTransaction = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{second}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, deletion.DetachedTransactionCount);
        Assert.True(deletion.IsDeleted);
        Assert.Empty(live.Tags);
        Assert.True(Assert.Single(all.Tags).IsDeleted);
        Assert.Empty(firstTransaction!.Data!.Tags);
        Assert.Empty(secondTransaction!.Data!.Tags);
    }

    [FunctionalFact]
    public async Task GivenConfiguredMaximum_WhenMoreTagsAttached_ThenMaximumIsStated()
    {
        await using var factory = CreateFactory(maximumTags: 1);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var first = await CreateTagAsync(client, "First");
        var second = await CreateTagAsync(client, "Second");
        var transactionId = await CreateTransactionAsync(client, "Limited expense");
        (await client.PostAsync($"/api/transactions/{transactionId}/tags/{first.Id}", null))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync(
            $"/api/transactions/{transactionId}/tags/{second.Id}",
            null);
        var body = await response.Content.ReadAsStringAsync();
        var record = await CreateTransactionResponseAsync(
            client,
            "Overconfigured expense",
            tags: ["First", "Second"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(TagMessages.MaximumExceeded, body, StringComparison.Ordinal);
        Assert.Contains(TagMessages.MaximumAllowed(1), body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, record.StatusCode);
        Assert.Contains(
            TransactionMessages.MaximumTagsAllowed(1),
            await record.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenForeignDeletedOrUnauthorizedRecords_WhenAssigned_ThenRequestIsHidden()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var tag = await CreateTagAsync(owner, "Private");
        var transactionId = await CreateTransactionAsync(owner, "Private expense");
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var otherTag = await CreateTagAsync(other, "Other");
        var otherTransaction = await CreateTransactionAsync(other, "Other expense");

        var foreignTag = await owner.PostAsync(
            $"/api/transactions/{transactionId}/tags/{otherTag.Id}",
            null);
        var foreignTransaction = await owner.PostAsync(
            $"/api/transactions/{otherTransaction}/tags/{tag.Id}",
            null);
        (await owner.DeleteAsync($"/api/transactions/{transactionId}"))
            .EnsureSuccessStatusCode();
        var deletedTransaction = await owner.PostAsync(
            $"/api/transactions/{transactionId}/tags/{tag.Id}",
            null);
        (await owner.DeleteAsync($"/api/tags/{tag.Id}"))
            .EnsureSuccessStatusCode();
        var deletedTag = await owner.PostAsync(
            $"/api/transactions/{Guid.NewGuid()}/tags/{tag.Id}",
            null);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var anonymousList = await anonymous.GetAsync("/api/tags");
        var administratorCreate = await administrator.PostAsJsonAsync(
            "/api/tags",
            new { Name = "Forbidden" });

        Assert.All(
            new[] { foreignTag, foreignTransaction, deletedTransaction, deletedTag },
            item => Assert.Equal(HttpStatusCode.NotFound, item.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorCreate.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(int maximumTags = 50)
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
                services.RemoveAll<TagOptions>();
                services.AddSingleton(new TagOptions(maximumTags));
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

    private static async Task<TagData> CreateTagAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/tags", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TagEnvelope>())!.Data!;
    }

    private static async Task<Guid> CreateTransactionAsync(HttpClient client, string description)
    {
        var response = await CreateTransactionResponseAsync(client, description);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionEnvelope>())!.Data!.Id;
    }

    private static async Task<HttpResponseMessage> CreateTransactionResponseAsync(
        HttpClient client,
        string description,
        IReadOnlyCollection<string>? tags = null)
    {
        var category = await CreateCategoryAsync(client);
        var account = await CreateAccountAsync(client);
        return await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = new DateOnly(2026, 9, 6),
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = account,
            CategoryId = category,
            Description = description,
            Tags = tags
        });
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = $"Category {Guid.NewGuid():N}" });
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
    private sealed record TagEnvelope(TagData? Data);
    private sealed record TagListEnvelope(TagListData? Data);
    private sealed record TagListData(IReadOnlyList<TagData> Tags);
    private sealed record TagData(
        Guid Id,
        string Name,
        bool IsDeleted,
        int DetachedTransactionCount);
    private sealed record TransactionEnvelope(TransactionData? Data);
    private sealed record TransactionData(Guid Id, IReadOnlyList<TagData> Tags);
}
