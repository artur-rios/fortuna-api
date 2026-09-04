using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
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

public sealed class FinancialAccountUpdateTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenValidDetails_WhenUpdated_ThenOnlyEditableFieldsChangeAndAuditSucceeds()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var original = await CreateAccountAsync(
            client,
            "Before",
            "Old Bank",
            FinancialAccountType.Checking,
            "USD",
            -25);

        var response = await client.PutAsJsonAsync($"/api/accounts/{original.Id}", new
        {
            Name = "  After  ",
            Institution = "  New Bank  ",
            AccountType = FinancialAccountType.Savings
        });
        var envelope = await response.Content.ReadFromJsonAsync<AccountEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original.Id, envelope?.Data?.Id);
        Assert.Equal("After", envelope?.Data?.Name);
        Assert.Equal("New Bank", envelope?.Data?.Institution);
        Assert.Equal(FinancialAccountType.Savings, envelope?.Data?.AccountType);
        Assert.Equal("USD", envelope?.Data?.CurrencyCode);
        Assert.Equal(-25, envelope?.Data?.OpeningBalance);
        Assert.True((envelope!.Data!.CreatedAt - original.CreatedAt).Duration() <
            TimeSpan.FromMilliseconds(1));
        Assert.True(envelope?.Data?.UpdatedAt >= original.UpdatedAt);
        Assert.Contains(FinancialAccountMessages.UpdatedSuccessfully, envelope.Messages);
        await using var context = CreateContext();
        var stored = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == original.Id);
        Assert.Equal(subject.ToString("D"), stored.User.ExternalSubject);
        Assert.Equal("AFTER", stored.NormalizedName);
        Assert.Equal("New Bank", stored.Institution);
        Assert.Equal(FinancialAccountType.Savings, stored.AccountType);
        Assert.Equal("USD", stored.Currency.Code);
        Assert.Equal(-25, stored.OpeningBalance);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateFinancialAccountCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("FinancialAccount", audit.EntityType);
        Assert.Equal(original.Id, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenImmutableFields_WhenUpdated_ThenEachAttemptIsRejectedAndAudited()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateAccountAsync(client, "Fixed", "Bank");

        var owner = await client.PutAsJsonAsync($"/api/accounts/{original.Id}", new
        {
            Name = "Fixed",
            Institution = "Bank",
            AccountType = FinancialAccountType.Checking,
            OwnerId = Guid.NewGuid()
        });
        var currency = await client.PutAsJsonAsync($"/api/accounts/{original.Id}", new
        {
            Name = "Fixed",
            Institution = "Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "USD"
        });
        var balance = await client.PutAsJsonAsync($"/api/accounts/{original.Id}", new
        {
            Name = "Fixed",
            Institution = "Bank",
            AccountType = FinancialAccountType.Checking,
            OpeningBalance = 999m
        });

        Assert.Equal(HttpStatusCode.BadRequest, owner.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, currency.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, balance.StatusCode);
        Assert.Contains(FinancialAccountMessages.OwnerImmutable, await owner.Content.ReadAsStringAsync());
        Assert.Contains(FinancialAccountMessages.CurrencyImmutable, await currency.Content.ReadAsStringAsync());
        Assert.Contains(FinancialAccountMessages.OpeningBalanceImmutable, await balance.Content.ReadAsStringAsync());
        await using var context = CreateContext();
        var stored = await context.FinancialAccounts.SingleAsync(item => item.PublicId == original.Id);
        Assert.Equal("Fixed", stored.Name);
        Assert.Equal(original.OpeningBalance, stored.OpeningBalance);
        var audits = await context.AuditEntries
            .Where(item => item.Operation == "UpdateFinancialAccountCommand")
            .ToArrayAsync();
        Assert.Equal(3, audits.Length);
        Assert.All(audits, audit => Assert.Equal(AuditOutcome.Refused, audit.Outcome));
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveName_WhenUpdated_ThenConflictLeavesAccountUnchanged()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(client, "Primary");
        var secondary = await CreateAccountAsync(client, "Secondary");

        var response = await client.PutAsJsonAsync($"/api/accounts/{secondary.Id}", new
        {
            Name = " primary ",
            Institution = "New Bank",
            AccountType = FinancialAccountType.Savings
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var context = CreateContext();
        var stored = await context.FinancialAccounts.SingleAsync(item => item.PublicId == secondary.Id);
        Assert.Equal("Secondary", stored.Name);
        Assert.Null(stored.Institution);
        Assert.Equal(FinancialAccountType.Checking, stored.AccountType);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateFinancialAccountCommand");
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
        Assert.Equal(FinancialAccountMessages.DuplicateName, audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignAccount_WhenUpdated_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var deleted = await CreateAccountAsync(ownerClient, "Deleted");
        var foreign = await CreateAccountAsync(ownerClient, "Foreign");
        await using (var context = CreateContext())
        {
            var stored = await context.FinancialAccounts.SingleAsync(item => item.PublicId == deleted.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var otherClient = factory.CreateClient();
        Authorize(otherClient, Guid.NewGuid(), HeimdallRoles.User);

        var deletedResponse = await ownerClient.PutAsJsonAsync(
            $"/api/accounts/{deleted.Id}",
            UpdateBody("Changed"));
        var foreignResponse = await otherClient.PutAsJsonAsync(
            $"/api/accounts/{foreign.Id}",
            UpdateBody("Changed"));

        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains(FinancialAccountMessages.NotFound, await deletedResponse.Content.ReadAsStringAsync());
        Assert.Contains(FinancialAccountMessages.NotFound, await foreignResponse.Content.ReadAsStringAsync());
        await using var assertionContext = CreateContext();
        Assert.False(await assertionContext.FinancialAccounts.AnyAsync(item => item.Name == "Changed"));
    }

    [FunctionalFact]
    public async Task GivenInvalidEditableFields_WhenUpdated_ThenBadRequestStoresNoChanges()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateAccountAsync(client, "Valid");

        var response = await client.PutAsJsonAsync($"/api/accounts/{original.Id}", new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FinancialAccountMessages.NameRequired, body, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.AccountTypeInvalid, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True(await context.FinancialAccounts.AnyAsync(item =>
            item.PublicId == original.Id && item.Name == "Valid"));
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenUpdated_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/accounts/{Guid.NewGuid()}",
            UpdateBody("Changed"));
        var administratorResponse = await administrator.PutAsJsonAsync(
            $"/api/accounts/{Guid.NewGuid()}",
            UpdateBody("Changed"));

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

    private static object UpdateBody(string name) => new
    {
        Name = name,
        Institution = (string?)null,
        AccountType = FinancialAccountType.Checking
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

    private sealed record AccountEnvelope(
        AccountData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record AccountData(
        Guid Id,
        string Name,
        string? Institution,
        FinancialAccountType AccountType,
        string CurrencyCode,
        decimal OpeningBalance,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
