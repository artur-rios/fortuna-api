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

public sealed class CounterpartyManagementTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenCounterparties_WhenManaged_ThenCrudAndNormalizedReuseAreApplied()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/counterparties",
            new { Name = " Corner Cafe " });
        var first = (await firstResponse.Content.ReadFromJsonAsync<CounterpartyEnvelope>())!.Data!;
        var reusedResponse = await client.PostAsJsonAsync(
            "/api/counterparties",
            new { Name = "corner cafe" });
        var reused = (await reusedResponse.Content.ReadFromJsonAsync<CounterpartyEnvelope>())!.Data!;
        var second = await CreateCounterpartyAsync(client, "Book Store");
        var duplicateUpdate = await client.PutAsJsonAsync(
            $"/api/counterparties/{second.Id}",
            new { Name = "CORNER CAFE" });
        var update = await client.PutAsJsonAsync(
            $"/api/counterparties/{second.Id}",
            new { Name = "Library" });
        var live = (await client.GetFromJsonAsync<CounterpartyListEnvelope>(
            "/api/counterparties"))!.Data!;
        var delete = await client.DeleteAsync($"/api/counterparties/{first.Id}");
        var all = (await client.GetFromJsonAsync<CounterpartyListEnvelope>(
            "/api/counterparties?includeDeleted=true"))!.Data!;

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reusedResponse.StatusCode);
        Assert.Equal(first.Id, reused.Id);
        Assert.True(reused.Reused);
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(new[] { "Corner Cafe", "Library" },
            live.Counterparties.Select(item => item.Name));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.True(all.Counterparties.Single(item => item.Id == first.Id).IsDeleted);
        Assert.Contains(
            CounterpartyMessages.ReusedSuccessfully,
            await reusedResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenSourceTransactions_WhenCounterpartiesMerged_ThenTransactionsAreReassigned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, subject, HeimdallRoles.User);
        var source = await CreateCounterpartyAsync(client, "Old Merchant");
        var target = await CreateCounterpartyAsync(client, "New Merchant");
        var categoryId = await CreateCategoryAsync(client, "Shopping");
        var accountId = await CreateAccountAsync(client);
        var first = await CreateTransactionAsync(
            client, accountId, categoryId, source.Name, new DateOnly(2026, 9, 4), "First");
        var second = await CreateTransactionAsync(
            client, accountId, categoryId, source.Name, new DateOnly(2026, 9, 5), "Second");

        var response = await client.PostAsJsonAsync(
            $"/api/counterparties/{source.Id}/merge",
            new { TargetId = target.Id });
        var merge = (await response.Content.ReadFromJsonAsync<MergeEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, merge.ReassignedTransactionCount);
        await using var context = CreateContext();
        var transactions = await context.FinancialTransactions
            .Include(item => item.Counterparty)
            .Where(item => item.PublicId == first || item.PublicId == second)
            .ToArrayAsync();
        Assert.All(transactions, item => Assert.Equal(target.Id, item.Counterparty!.PublicId));
        var storedSource = await context.Counterparties.SingleAsync(item =>
            item.PublicId == source.Id);
        Assert.True(storedSource.IsDeleted);
        Assert.Equal(1, await context.AuditEntries.CountAsync(item =>
            item.EntityPublicId == source.Id &&
            item.Operation == "MergeCounterpartiesCommand" &&
            item.Outcome == AuditOutcome.Succeeded));
    }

    [FunctionalFact]
    public async Task GivenTransactionHistory_WhenCategorySuggested_ThenMostRecentCategoryIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var counterparty = await CreateCounterpartyAsync(client, "Market");
        var olderCategory = await CreateCategoryAsync(client, "Groceries");
        var recentCategory = await CreateCategoryAsync(client, "Household");
        var accountId = await CreateAccountAsync(client);
        await CreateTransactionAsync(
            client,
            accountId,
            recentCategory,
            counterparty.Name,
            new DateOnly(2026, 9, 5),
            "Recent");
        await CreateTransactionAsync(
            client,
            accountId,
            olderCategory,
            counterparty.Name,
            new DateOnly(2026, 9, 4),
            "Older created later");

        var response = await client.GetAsync(
            $"/api/counterparties/{counterparty.Id}/suggested-category");
        var suggestion = (await response.Content.ReadFromJsonAsync<SuggestionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(suggestion.HasSuggestion);
        Assert.Equal(recentCategory, suggestion.CategoryId);
        Assert.StartsWith("Household ", suggestion.CategoryName, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenNoHistory_WhenCategorySuggested_ThenNoSuggestionIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var counterparty = await CreateCounterpartyAsync(client, "Unused Merchant");

        var response = await client.GetAsync(
            $"/api/counterparties/{counterparty.Id}/suggested-category");
        var suggestion = (await response.Content.ReadFromJsonAsync<SuggestionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(suggestion.HasSuggestion);
        Assert.Null(suggestion.CategoryId);
        Assert.Contains(
            CounterpartyMessages.NoSuggestion,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenForeignDeletedOrUnauthorizedCounterparty_WhenAccessed_ThenItIsHidden()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var owned = await CreateCounterpartyAsync(owner, "Private Merchant");
        var target = await CreateCounterpartyAsync(owner, "Private Target");
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreign = await CreateCounterpartyAsync(other, "Foreign Merchant");

        var foreignUpdate = await owner.PutAsJsonAsync(
            $"/api/counterparties/{foreign.Id}",
            new { Name = "Stolen" });
        var foreignSuggestion = await owner.GetAsync(
            $"/api/counterparties/{foreign.Id}/suggested-category");
        var foreignMerge = await owner.PostAsJsonAsync(
            $"/api/counterparties/{owned.Id}/merge",
            new { TargetId = foreign.Id });
        (await owner.DeleteAsync($"/api/counterparties/{target.Id}"))
            .EnsureSuccessStatusCode();
        var deletedSuggestion = await owner.GetAsync(
            $"/api/counterparties/{target.Id}/suggested-category");
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var anonymousList = await anonymous.GetAsync("/api/counterparties");
        var administratorCreate = await administrator.PostAsJsonAsync(
            "/api/counterparties",
            new { Name = "Forbidden" });

        Assert.All(
            new[] { foreignUpdate, foreignSuggestion, foreignMerge, deletedSuggestion },
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

    private static async Task<CounterpartyData> CreateCounterpartyAsync(
        HttpClient client,
        string name)
    {
        var response = await client.PostAsJsonAsync("/api/counterparties", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CounterpartyEnvelope>())!.Data!;
    }

    private static async Task<Guid> CreateTransactionAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        string counterparty,
        DateOnly occurredOn,
        string description)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = occurredOn,
            Amount = 10m,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Counterparty = counterparty,
            Description = description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = $"{name} {Guid.NewGuid():N}" });
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
    private sealed record CounterpartyEnvelope(CounterpartyData? Data);
    private sealed record CounterpartyListEnvelope(CounterpartyListData? Data);
    private sealed record CounterpartyListData(IReadOnlyList<CounterpartyData> Counterparties);
    private sealed record CounterpartyData(
        Guid Id,
        string Name,
        bool IsDeleted,
        bool Reused);
    private sealed record MergeEnvelope(MergeData? Data);
    private sealed record MergeData(Guid SourceId, Guid TargetId, int ReassignedTransactionCount);
    private sealed record SuggestionEnvelope(SuggestionData? Data);
    private sealed record SuggestionData(
        Guid CounterpartyId,
        bool HasSuggestion,
        Guid? CategoryId,
        string? CategoryName);
}
