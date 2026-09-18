using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
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

public sealed class RecurringTransactionListTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenRuleDefinedEarlier_WhenListed_ThenItIsFoundWithItsScheduleAndRemainsReachableById()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var created = await DefineRuleAsync(client, subject, "Rent", 100m);

        // When
        var page = await client.GetFromJsonAsync<RulePage>("/api/recurring-transactions");
        var listed = Assert.Single(page!.Data);
        var byId = await client.GetAsync($"/api/recurring-transactions/{listed.Id}");

        // Then
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(created.NextOccurrences, listed.NextOccurrences);
        Assert.Equal("BRL", listed.CurrencyCode);
        Assert.Equal(100m, listed.Amount);
        Assert.False(listed.IsDeleted);
        Assert.Equal(1, page.TotalItems);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Contains(RecurringTransactionMessages.ListedSuccessfully, page.Messages);
    }

    [FunctionalFact]
    public async Task GivenAnotherUsersRule_WhenListed_ThenItIsNeverVisible()
    {
        // Given
        var ownerSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, ownerSubject, HeimdallRoles.User);
        var owned = await DefineRuleAsync(ownerClient, ownerSubject, "Private", 40m);
        var otherSubject = Guid.NewGuid();
        using var otherClient = factory.CreateClient();
        Authorize(otherClient, otherSubject, HeimdallRoles.User);
        await DefineRuleAsync(otherClient, otherSubject, "Theirs", 70m);

        // When
        var otherPage = await otherClient.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?IncludeDeleted=true");

        // Then
        Assert.DoesNotContain(otherPage!.Data, rule => rule.Id == owned.Id);
        Assert.Equal(1, otherPage.TotalItems);
        Assert.Equal(70m, Assert.Single(otherPage.Data).Amount);
    }

    [FunctionalFact]
    public async Task GivenDeletedRule_WhenListed_ThenExplicitInclusionControlsVisibility()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var live = await DefineRuleAsync(client, subject, "Live", 10m);
        var archived = await DefineRuleAsync(client, subject, "Archived", 20m);
        (await client.DeleteAsync($"/api/recurring-transactions/{archived.Id}"))
            .EnsureSuccessStatusCode();

        // When
        var hidden = await client.GetFromJsonAsync<RulePage>("/api/recurring-transactions");
        var included = await client.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?IncludeDeleted=true");

        // Then
        Assert.Equal(live.Id, Assert.Single(hidden!.Data).Id);
        Assert.Equal(2, included!.TotalItems);
        Assert.True(included.Data.Single(rule => rule.Id == archived.Id).IsDeleted);
        Assert.False(included.Data.Single(rule => rule.Id == live.Id).IsDeleted);

        await using var context = CreateContext();
        Assert.True((await context.RecurringTransactions
            .SingleAsync(item => item.PublicId == archived.Id)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenEndedRule_WhenListedAsActive_ThenOnlyRunningRulesAreReturned()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var running = await DefineRuleAsync(client, subject, "Running", 10m);
        var ended = await DefineRuleAsync(
            client,
            subject,
            "Ended",
            20m,
            startsOn: Today.AddMonths(-6),
            endsOn: Today.AddMonths(-1));

        // When
        var active = await client.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?Active=true");
        var inactive = await client.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?Active=false");
        var all = await client.GetFromJsonAsync<RulePage>("/api/recurring-transactions");

        // Then
        Assert.Equal(running.Id, Assert.Single(active!.Data).Id);
        Assert.Equal(ended.Id, Assert.Single(inactive!.Data).Id);
        Assert.Equal(2, all!.TotalItems);
    }

    [FunctionalFact]
    public async Task GivenNoRules_WhenListed_ThenAnEmptyPageIsReturned()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);

        // When
        var response = await client.GetAsync("/api/recurring-transactions");
        var page = await response.Content.ReadFromJsonAsync<RulePage>();

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(page!.Data);
        Assert.Equal(0, page.TotalItems);
    }

    [FunctionalFact]
    public async Task GivenSortAndPageSize_WhenListed_ThenOrderingAndPageMetadataApply()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await DefineRuleAsync(client, subject, "Small", 10m);
        await DefineRuleAsync(client, subject, "Large", 300m);
        await DefineRuleAsync(client, subject, "Medium", 50m);

        // When
        var first = await client.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?SortBy=Amount&Descending=true&PageNumber=1&PageSize=2");
        var second = await client.GetFromJsonAsync<RulePage>(
            "/api/recurring-transactions?SortBy=Amount&Descending=true&PageNumber=2&PageSize=2");

        // Then
        Assert.Equal([300m, 50m], first!.Data.Select(rule => rule.Amount));
        Assert.Equal([10m], second!.Data.Select(rule => rule.Amount));
        Assert.Equal(3, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenListed_ThenUnauthorizedIsReturned()
    {
        // Given
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // When
        var response = await client.GetAsync("/api/recurring-transactions");

        // Then
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedFilter_WhenListed_ThenBadRequestNamesTheField()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);

        // When
        var response = await client.GetAsync("/api/recurring-transactions?Counterparty=Landlord");
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            RecurringTransactionMessages.UnsupportedFilter("Counterparty"),
            body,
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedSortField_WhenListed_ThenBadRequestIsReturned()
    {
        // Given
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);

        // When
        var response = await client.GetAsync("/api/recurring-transactions?SortBy=Description");
        var body = await response.Content.ReadAsStringAsync();

        // Then
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            RecurringTransactionMessages.SortByUnsupported,
            body,
            StringComparison.Ordinal);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<RuleData> DefineRuleAsync(
        HttpClient client,
        Guid subject,
        string description,
        decimal amount,
        DateOnly? startsOn = null,
        DateOnly? endsOn = null)
    {
        var account = await CreateAccountAsync(client, $"{description} account");
        var category = await SeedCategoryAsync(subject, $"{description} category");
        var response = await client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = account,
            CategoryId = category,
            Direction = TransactionDirection.Expense,
            Amount = amount,
            Frequency = RecurrenceFrequency.Monthly,
            StartsOn = startsOn ?? Today,
            EndsOn = endsOn,
            Description = description
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RuleEnvelope>())!.Data!;
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

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = "Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = 1000m
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task EnsureProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options,
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        DatabaseDiagnosticsOptions.Disabled);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Rule Owner"
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

    private sealed record RuleEnvelope(RuleData? Data);

    private sealed record RulePage(
        IReadOnlyList<RuleData> Data,
        IReadOnlyCollection<string> Messages,
        int PageNumber,
        int PageSize,
        int TotalItems,
        int TotalPages);

    private sealed record RuleData(
        Guid Id,
        decimal Amount,
        string CurrencyCode,
        DateOnly StartsOn,
        DateOnly? EndsOn,
        string? Description,
        bool IsDeleted,
        IReadOnlyCollection<DateOnly> NextOccurrences);

    private sealed record IdEnvelope(IdData? Data);

    private sealed record IdData(Guid Id);
}
