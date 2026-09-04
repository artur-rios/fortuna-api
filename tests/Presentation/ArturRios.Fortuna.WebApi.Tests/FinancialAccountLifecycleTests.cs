using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Command.Input;
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

public sealed class FinancialAccountLifecycleTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenAccountWithTransactions_WhenDeletedAndRestored_ThenOnlyCascadeIsReversed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Lifecycle", 100m);
        var transactionIds = await AddTransactionsAsync(account.Id, preDeleteFirst: true);

        var deleted = await client.DeleteAsync($"/api/accounts/{account.Id}");
        var hidden = await client.GetAsync($"/api/accounts/{account.Id}");
        var retained = await client.GetAsync($"/api/accounts/{account.Id}?includeDeleted=true");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);

        await using (var context = CreateContext())
        {
            var storedAccount = await context.FinancialAccounts.SingleAsync(item => item.PublicId == account.Id);
            var transactions = await context.FinancialTransactions
                .Where(item => transactionIds.Contains(item.PublicId))
                .OrderBy(item => item.PublicId)
                .ToListAsync();
            Assert.True(storedAccount.IsDeleted);
            Assert.All(transactions, item => Assert.True(item.IsDeleted));
            Assert.Single(transactions, item => item.DeletionCascadeId == storedAccount.DeletionCascadeId);
            Assert.Single(transactions, item => item.DeletionCascadeId != storedAccount.DeletionCascadeId);
        }

        var restored = await client.PostAsync($"/api/accounts/{account.Id}/restore", null);
        var balance = await client.GetFromJsonAsync<BalanceEnvelope>($"/api/accounts/{account.Id}/balance");

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(110m, balance?.Data?.Balance);
        await using (var context = CreateContext())
        {
            var transactions = await context.FinancialTransactions
                .Where(item => transactionIds.Contains(item.PublicId))
                .ToListAsync();
            Assert.Single(transactions, item => !item.IsDeleted);
            Assert.Single(transactions, item => item.IsDeleted);
        }
    }

    [FunctionalFact]
    public async Task GivenLiveAccount_WhenHardDeleteRequested_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Live", 0m);

        var response = await client.DeleteAsync($"/api/accounts/{account.Id}/hard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(FinancialAccountMessages.HardDeleteRequiresSoftDeletion, body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenLiveAccount_WhenRestoreRequested_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Never Deleted", 0m);

        var response = await client.PostAsync($"/api/accounts/{account.Id}/restore", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(FinancialAccountMessages.RestoreRequiresSoftDeletion, body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedAccountWithLiveReference_WhenHardDeleted_ThenReferenceIsNamed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Referenced", 0m);
        var transactionId = (await AddTransactionsAsync(account.Id))[0];
        (await client.DeleteAsync($"/api/accounts/{account.Id}")).EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            var transaction = await context.FinancialTransactions
                .SingleAsync(item => item.PublicId == transactionId);
            transaction.Restore(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/accounts/{account.Id}/hard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("live transactions", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedUnreferencedAccount_WhenHardDeleted_ThenRowsDisappearAndAuditSurvives()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Permanent", 0m);
        var transactionIds = await AddTransactionsAsync(account.Id, preDeleteFirst: true);
        (await client.DeleteAsync($"/api/accounts/{account.Id}")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/accounts/{account.Id}/hard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync(item => item.PublicId == account.Id));
        Assert.False(await context.FinancialTransactions.AnyAsync(item => transactionIds.Contains(item.PublicId)));
        var audit = await context.AuditEntries
            .Where(item => item.EntityPublicId == account.Id)
            .ToListAsync();
        Assert.Contains(audit, item => item.Operation == nameof(DeleteFinancialAccountCommand));
        Assert.Contains(audit, item => item.Operation == nameof(HardDeleteFinancialAccountCommand));
        Assert.All(audit, item => Assert.Equal("FinancialAccount", item.EntityType));
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveName_WhenAccountRestored_ThenConflictLeavesItDeleted()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateAccountAsync(client, "Reused", 0m);
        (await client.DeleteAsync($"/api/accounts/{original.Id}")).EnsureSuccessStatusCode();
        await CreateAccountAsync(client, "Reused", 0m);

        var response = await client.PostAsync($"/api/accounts/{original.Id}/restore", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(FinancialAccountMessages.DuplicateName, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True((await context.FinancialAccounts.SingleAsync(item => item.PublicId == original.Id)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrUnauthorizedAccount_WhenLifecycleRequested_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Private", 0m);
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreign = await other.DeleteAsync($"/api/accounts/{account.Id}");
        var missing = await other.DeleteAsync($"/api/accounts/{Guid.NewGuid()}");
        var anonymousResponse = await anonymous.DeleteAsync($"/api/accounts/{account.Id}");
        var administratorResponse = await administrator.DeleteAsync($"/api/accounts/{account.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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

    private async Task<IReadOnlyList<Guid>> AddTransactionsAsync(
        Guid accountId,
        bool preDeleteFirst = false)
    {
        await using var context = CreateContext();
        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .SingleAsync(item => item.PublicId == accountId);
        var first = new FinancialTransaction(
            account.User,
            account,
            TransactionDirection.Earning,
            50m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        var second = new FinancialTransaction(
            account.User,
            account,
            TransactionDirection.Earning,
            10m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        if (preDeleteFirst)
        {
            first.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.FinancialTransactions.AddRange(first, second);
        await context.SaveChangesAsync();
        return [first.PublicId, second.PublicId];
    }

    private static async Task<AccountData> CreateAccountAsync(
        HttpClient client,
        string name,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
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
    private sealed record BalanceEnvelope(BalanceData? Data);
    private sealed record BalanceData(decimal Balance);
}
