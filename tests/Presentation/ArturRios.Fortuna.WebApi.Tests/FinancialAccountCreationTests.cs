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

public sealed class FinancialAccountCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenValidOverdrawnAccount_WhenCreated_ThenOwnedAccountAndAuditAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/accounts", Command(
            "  Daily Account  ",
            "  Example Bank  ",
            FinancialAccountType.Checking,
            "brl",
            -125.45m));
        var envelope = await response.Content.ReadFromJsonAsync<AccountEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.NotEqual(Guid.Empty, envelope.Data.Id);
        Assert.Equal("Daily Account", envelope.Data.Name);
        Assert.Equal("Example Bank", envelope.Data.Institution);
        Assert.Equal(FinancialAccountType.Checking, envelope.Data.AccountType);
        Assert.Equal("BRL", envelope.Data.CurrencyCode);
        Assert.Equal(-125.45m, envelope.Data.OpeningBalance);
        Assert.Equal(envelope.Data.CreatedAt, envelope.Data.UpdatedAt);
        await using var context = CreateContext();
        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == envelope.Data.Id);
        Assert.Equal(subject.ToString("D"), account.User.ExternalSubject);
        Assert.Equal("DAILY ACCOUNT", account.NormalizedName);
        Assert.Equal("BRL", account.Currency.Code);
        Assert.Equal(-125.45m, account.OpeningBalance);
        Assert.False(account.IsDeleted);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "CreateFinancialAccountCommand");
        Assert.Equal(account.User.PublicId, audit.ActorUserId);
        Assert.Equal("FinancialAccount", audit.EntityType);
        Assert.Equal(account.PublicId, audit.EntityPublicId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Null(audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveName_WhenCreated_ThenConflictIsAudited()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var first = await client.PostAsJsonAsync("/api/accounts", Command(
            "Household", null, FinancialAccountType.Checking, "BRL", 10));
        var duplicate = await client.PostAsJsonAsync("/api/accounts", Command(
            "  household  ", "Other Bank", FinancialAccountType.Savings, "BRL", 20));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(1, await context.FinancialAccounts.CountAsync());
        var audits = await context.AuditEntries
            .Where(item => item.Operation == "CreateFinancialAccountCommand")
            .OrderBy(item => item.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.Contains(audits, item => item.Outcome == AuditOutcome.Succeeded);
        var refused = Assert.Single(audits, item => item.Outcome == AuditOutcome.Refused);
        Assert.Equal(FinancialAccountMessages.DuplicateName, refused.Reason);
    }

    [FunctionalFact]
    public async Task GivenDuplicateSoftDeletedName_WhenCreated_ThenNewAccountIsAllowed()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var firstResponse = await client.PostAsJsonAsync("/api/accounts", Command(
            "Travel", null, FinancialAccountType.Cash, "USD", 50));
        var first = (await firstResponse.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!;
        await using (var context = CreateContext())
        {
            var account = await context.FinancialAccounts.SingleAsync(item => item.PublicId == first.Id);
            account.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var replacement = await client.PostAsJsonAsync("/api/accounts", Command(
            "travel", null, FinancialAccountType.Cash, "USD", 75));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        await using var assertionContext = CreateContext();
        var accounts = await assertionContext.FinancialAccounts
            .Where(item => item.NormalizedName == "TRAVEL")
            .OrderBy(item => item.IsDeleted)
            .ToArrayAsync();
        Assert.Equal(2, accounts.Length);
        Assert.Single(accounts, item => item.IsDeleted);
        Assert.Single(accounts, item => !item.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenSameNameForDifferentUsers_WhenCreated_ThenBothAccountsAreAllowed()
    {
        await using var factory = CreateFactory();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(secondClient, Guid.NewGuid(), HeimdallRoles.User);

        var results = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/accounts", Command(
                "Everyday", null, FinancialAccountType.Checking, "BRL", 0)),
            secondClient.PostAsJsonAsync("/api/accounts", Command(
                "Everyday", null, FinancialAccountType.Checking, "BRL", 0)));

        Assert.All(results, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var context = CreateContext();
        Assert.Equal(2, await context.FinancialAccounts.CountAsync());
        Assert.Equal(2, await context.FinancialAccounts.Select(item => item.UserId).Distinct().CountAsync());
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
            firstClient.PostAsJsonAsync("/api/accounts", Command(
                "Concurrent", null, FinancialAccountType.Checking, "BRL", 1)),
            secondClient.PostAsJsonAsync("/api/accounts", Command(
                "concurrent", null, FinancialAccountType.Checking, "BRL", 2)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        await using var context = CreateContext();
        Assert.Equal(1, await context.FinancialAccounts.CountAsync());
        Assert.Equal(2, await context.AuditEntries.CountAsync(item =>
            item.Operation == "CreateFinancialAccountCommand"));
    }

    [FunctionalFact]
    public async Task GivenMissingRequiredFields_WhenCreated_ThenBadRequestNamesFields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/accounts", new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(FinancialAccountMessages.NameRequired, body, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.AccountTypeInvalid, body, StringComparison.Ordinal);
        Assert.Contains(FinancialAccountMessages.CurrencyRequired, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenUnknownCurrency_WhenCreated_ThenBadRequestNamesCurrency()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/accounts", Command(
            "Unknown Currency", null, FinancialAccountType.Other, "ZZZ", 0));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown currency code 'ZZZ'.", body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenExcessOpeningBalancePrecision_WhenCreated_ThenBadRequestIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/accounts", Command(
            "Precise", null, FinancialAccountType.Other, "BRL", 1.12345m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenCreated_ThenUnauthorizedStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/accounts", Command(
            "Hidden", null, FinancialAccountType.Cash, "BRL", 0));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenCreated_ThenForbiddenStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync("/api/accounts", Command(
            "Admin Account", null, FinancialAccountType.Cash, "BRL", 0));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.FinancialAccounts.AnyAsync());
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

    private static object Command(
        string name,
        string? institution,
        FinancialAccountType accountType,
        string currencyCode,
        decimal openingBalance) => new
        {
            Name = name,
            Institution = institution,
            AccountType = accountType,
            CurrencyCode = currencyCode,
            OpeningBalance = openingBalance
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

    private sealed record AccountEnvelope(AccountData? Data);

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
