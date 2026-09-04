using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
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

public sealed class FinancialAccountViewTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedAccount_WhenReadById_ThenItsDetailsAreReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(
            client,
            "Daily",
            "Example Bank",
            FinancialAccountType.Checking,
            "BRL",
            -12.34m);

        var response = await client.GetAsync($"/api/accounts/{account.Id}");
        var envelope = await response.Content.ReadFromJsonAsync<AccountEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(account.Id, envelope?.Data?.Id);
        Assert.Equal(account.Name, envelope?.Data?.Name);
        Assert.Equal(account.Institution, envelope?.Data?.Institution);
        Assert.Equal(account.AccountType, envelope?.Data?.AccountType);
        Assert.Equal(account.CurrencyCode, envelope?.Data?.CurrencyCode);
        Assert.Equal(account.OpeningBalance, envelope?.Data?.OpeningBalance);
        Assert.False(envelope?.Data?.IsDeleted);
        Assert.Contains(FinancialAccountMessages.RetrievedSuccessfully, envelope!.Messages);
    }

    [FunctionalFact]
    public async Task GivenMissingOrForeignAccount_WhenReadById_ThenResponsesAreIdenticalNotFound()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(ownerClient, "Private");
        using var otherClient = factory.CreateClient();
        Authorize(otherClient, Guid.NewGuid(), HeimdallRoles.User);

        var foreign = await otherClient.GetAsync($"/api/accounts/{account.Id}");
        var missing = await otherClient.GetAsync($"/api/accounts/{Guid.NewGuid()}");
        var foreignBody = await foreign.Content.ReadAsStringAsync();
        var missingBody = await missing.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(FinancialAccountMessages.NotFound, foreignBody, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.NotFound, missingBody, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedAccount_WhenReadOrListed_ThenExplicitInclusionControlsVisibility()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Archived");
        await using (var context = CreateContext())
        {
            var stored = await context.FinancialAccounts.SingleAsync(item => item.PublicId == account.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var hiddenDetail = await client.GetAsync($"/api/accounts/{account.Id}");
        var visibleDetail = await client.GetAsync($"/api/accounts/{account.Id}?includeDeleted=true");
        var hiddenList = await client.GetFromJsonAsync<AccountPage>("/api/accounts");
        var visibleList = await client.GetFromJsonAsync<AccountPage>("/api/accounts?IncludeDeleted=true");

        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, visibleDetail.StatusCode);
        Assert.Empty(hiddenList!.Data);
        Assert.True(Assert.Single(visibleList!.Data).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenFiltersAndSort_WhenListed_ThenOnlyOwnedMatchesAreReturnedInOrder()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(
            client,
            "Reserve One",
            "Example Bank",
            FinancialAccountType.Savings,
            "USD",
            10);
        await CreateAccountAsync(
            client,
            "Reserve Two",
            "Example Bank",
            FinancialAccountType.Savings,
            "USD",
            20);
        await CreateAccountAsync(
            client,
            "Daily",
            "Other Bank",
            FinancialAccountType.Checking,
            "BRL",
            30);
        using var foreignClient = factory.CreateClient();
        Authorize(foreignClient, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(
            foreignClient,
            "Reserve Foreign",
            "Example Bank",
            FinancialAccountType.Savings,
            "USD",
            40);

        var response = await client.GetAsync(
            "/api/accounts?Name=reserve&Institution=AMPLE&AccountType=2&CurrencyCode=usd" +
            "&SortBy=OpeningBalance&Descending=true&PageNumber=1&PageSize=10");
        var page = await response.Content.ReadFromJsonAsync<AccountPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page?.TotalItems);
        Assert.Equal([20m, 10m], page!.Data.Select(item => item.OpeningBalance));
        Assert.Contains(FinancialAccountMessages.ListedSuccessfully, page.Messages);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedSortOrFilter_WhenListed_ThenBadRequestNamesTheField()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var sort = await client.GetAsync("/api/accounts?SortBy=Balance");
        var filter = await client.GetAsync("/api/accounts?Balance=10");
        var sortBody = await sort.Content.ReadAsStringAsync();
        var filterBody = await filter.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, filter.StatusCode);
        Assert.Contains("SortBy", sortBody, StringComparison.Ordinal);
        Assert.Contains("Balance", filterBody, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenOversizedPage_WhenListed_ThenPageSizeIsClampedAndReported()
    {
        await using var factory = CreateFactory(maximumPageSize: 2);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(client, "One");
        await CreateAccountAsync(client, "Two");
        await CreateAccountAsync(client, "Three");

        var response = await client.GetAsync("/api/accounts?PageSize=999");
        var page = await response.Content.ReadFromJsonAsync<AccountPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page?.PageSize);
        Assert.Equal(3, page?.TotalItems);
        Assert.Equal(2, page?.Data.Count);
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenViewed_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousList = await anonymous.GetAsync("/api/accounts");
        var administratorList = await administrator.GetAsync("/api/accounts");
        var anonymousDetail = await anonymous.GetAsync($"/api/accounts/{Guid.NewGuid()}");
        var administratorDetail = await administrator.GetAsync($"/api/accounts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDetail.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(int maximumPageSize = 100)
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
                services.RemoveAll<PaginationOptions>();
                services.AddSingleton(new PaginationOptions(maximumPageSize));
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

    private static async Task<AccountData> CreateAccountAsync(
        HttpClient client,
        string name,
        string? institution = null,
        FinancialAccountType accountType = FinancialAccountType.Checking,
        string currencyCode = "BRL",
        decimal openingBalance = 0)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = institution,
            AccountType = accountType,
            CurrencyCode = currencyCode,
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!;
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

    private sealed record AccountEnvelope(
        AccountData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record AccountPage(
        IReadOnlyList<AccountData> Data,
        IReadOnlyCollection<string> Messages,
        int PageNumber,
        int PageSize,
        int TotalItems,
        int TotalPages);

    private sealed record AccountData(
        Guid Id,
        string Name,
        string? Institution,
        FinancialAccountType AccountType,
        string CurrencyCode,
        decimal OpeningBalance,
        bool IsDeleted,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
