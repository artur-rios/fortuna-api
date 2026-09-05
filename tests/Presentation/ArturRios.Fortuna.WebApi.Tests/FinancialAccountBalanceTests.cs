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

public sealed class FinancialAccountBalanceTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLiveTransactions_WhenBalanceRequestedAsOfDate_ThenExactBalanceIsReturned()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Daily", "BRL", 100.1000m);
        await AddTransactionsAsync(
            account.Id,
            (TransactionDirection.Earning, 25.5555m, today, false),
            (TransactionDirection.Expense, 10.1111m, today, false),
            (TransactionDirection.Earning, 999.9999m, today, true),
            (TransactionDirection.Earning, 50m, today.AddDays(1), false));

        var response = await client.GetAsync($"/api/accounts/{account.Id}/balance?asOf={today:yyyy-MM-dd}");
        var envelope = await response.Content.ReadFromJsonAsync<BalanceEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(account.Id, envelope?.Data?.Id);
        Assert.Equal("BRL", envelope?.Data?.CurrencyCode);
        Assert.Equal(115.5444m, envelope?.Data?.Balance);
        Assert.Equal(today, envelope?.Data?.AsOf);
        Assert.Contains(FinancialAccountMessages.BalanceRetrievedSuccessfully, envelope!.Messages);
    }

    [FunctionalFact]
    public async Task GivenNoTransactions_WhenBalanceRequested_ThenOpeningBalanceIsReturned()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Cash", "USD", -12.3456m);

        var response = await client.GetAsync($"/api/accounts/{account.Id}/balance");
        var envelope = await response.Content.ReadFromJsonAsync<BalanceEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(-12.3456m, envelope?.Data?.Balance);
        Assert.Equal("USD", envelope?.Data?.CurrencyCode);
        Assert.Equal(today, envelope?.Data?.AsOf);
    }

    [FunctionalFact]
    public async Task GivenDateBeforeAccountOpened_WhenBalanceRequested_ThenOpeningBalanceIsReturned()
    {
        var asOf = new DateOnly(2000, 1, 1);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Historical", "BRL", 42m);
        await AddTransactionsAsync(
            account.Id,
            (TransactionDirection.Earning, 100m, asOf.AddDays(-1), false));

        var response = await client.GetAsync($"/api/accounts/{account.Id}/balance?asOf={asOf:yyyy-MM-dd}");
        var envelope = await response.Content.ReadFromJsonAsync<BalanceEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(42m, envelope?.Data?.Balance);
        Assert.Equal(asOf, envelope?.Data?.AsOf);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrDeletedAccount_WhenBalanceRequested_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var foreign = await CreateAccountAsync(ownerClient, "Private", "BRL", 1m);
        var deleted = await CreateAccountAsync(ownerClient, "Deleted", "BRL", 2m);
        await using (var context = CreateContext())
        {
            var stored = await context.FinancialAccounts.SingleAsync(item => item.PublicId == deleted.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        using var otherClient = factory.CreateClient();
        Authorize(otherClient, Guid.NewGuid(), HeimdallRoles.User);
        var foreignResponse = await otherClient.GetAsync($"/api/accounts/{foreign.Id}/balance");
        var deletedResponse = await ownerClient.GetAsync($"/api/accounts/{deleted.Id}/balance");
        var missingResponse = await otherClient.GetAsync($"/api/accounts/{Guid.NewGuid()}/balance");
        var foreignBody = await foreignResponse.Content.ReadAsStringAsync();
        var deletedBody = await deletedResponse.Content.ReadAsStringAsync();
        var missingBody = await missingResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Contains(FinancialAccountMessages.NotFound, foreignBody, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.NotFound, deletedBody, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.NotFound, missingBody, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAttemptToSetBalance_WhenEndpointCalled_ThenMethodNotAllowedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Immutable Balance", "BRL", 10m);

        var response = await client.PutAsJsonAsync(
            $"/api/accounts/{account.Id}/balance",
            new { Balance = 999m });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenBalanceRequested_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var anonymousResponse = await anonymous.GetAsync($"/api/accounts/{id}/balance");
        var administratorResponse = await administrator.GetAsync($"/api/accounts/{id}/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
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

    private async Task AddTransactionsAsync(
        Guid accountId,
        params (TransactionDirection Direction, decimal Amount, DateOnly OccurredOn, bool Deleted)[] items)
    {
        await using var context = CreateContext();
        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == accountId);
        var category = new Category(account.User, "General", DateTimeOffset.UtcNow);
        foreach (var item in items)
        {
            var transaction = new FinancialTransaction(
                account.User,
                account,
                category,
                item.Direction,
                item.Amount,
                item.OccurredOn,
                DateTimeOffset.UtcNow);
            if (item.Deleted)
            {
                transaction.SoftDelete(DateTimeOffset.UtcNow);
            }

            context.FinancialTransactions.Add(transaction);
        }

        await context.SaveChangesAsync();
    }

    private static async Task<AccountData> CreateAccountAsync(
        HttpClient client,
        string name,
        string currencyCode,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            AccountType = FinancialAccountType.Checking,
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

    private sealed record AccountEnvelope(AccountData? Data);

    private sealed record AccountData(Guid Id);

    private sealed record BalanceEnvelope(
        BalanceData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record BalanceData(
        Guid Id,
        string CurrencyCode,
        decimal Balance,
        DateOnly AsOf);
}
