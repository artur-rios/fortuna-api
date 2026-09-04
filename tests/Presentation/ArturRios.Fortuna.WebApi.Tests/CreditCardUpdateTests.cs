using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
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

public sealed class CreditCardUpdateTests : IAsyncLifetime
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
        var original = await CreateCardAsync(client, "Before", "Old Bank", "USD", 1000m, 20, 5);

        var response = await client.PutAsJsonAsync(
            $"/api/credit-cards/{original.Id}",
            UpdateBody("  After  ", "  New Bank  ", 2500m, 28, 7));
        var envelope = await response.Content.ReadFromJsonAsync<CreditCardEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original.Id, envelope?.Data?.Id);
        Assert.Equal("After", envelope?.Data?.Name);
        Assert.Equal("New Bank", envelope?.Data?.Issuer);
        Assert.Equal("USD", envelope?.Data?.CurrencyCode);
        Assert.Equal(2500m, envelope?.Data?.CreditLimit);
        Assert.Equal((short)28, envelope?.Data?.ClosingDay);
        Assert.Equal((short)7, envelope?.Data?.DueDay);
        Assert.Equal("1234", envelope?.Data?.LastFourDigits);
        Assert.Contains(CreditCardMessages.UpdatedSuccessfully, envelope!.Messages);
        await using var context = CreateContext();
        var stored = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == original.Id);
        Assert.Equal(subject.ToString("D"), stored.User.ExternalSubject);
        Assert.Equal("AFTER", stored.NormalizedName);
        Assert.Equal("USD", stored.Currency.Code);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateCreditCardCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("CreditCard", audit.EntityType);
        Assert.Equal(original.Id, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenImmutableCurrency_WhenUpdated_ThenRequestIsRejectedAndAudited()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateCardAsync(client, "Fixed");

        var response = await client.PutAsJsonAsync($"/api/credit-cards/{original.Id}", new
        {
            Name = "Fixed",
            Issuer = "Example Bank",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 5,
            CurrencyCode = "USD"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CreditCardMessages.CurrencyImmutable, await response.Content.ReadAsStringAsync());
        await using var context = CreateContext();
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateCreditCardCommand");
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveName_WhenUpdated_ThenConflictLeavesCardUnchanged()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateCardAsync(client, "Primary");
        var secondary = await CreateCardAsync(client, "Secondary");

        var response = await client.PutAsJsonAsync(
            $"/api/credit-cards/{secondary.Id}",
            UpdateBody(" primary ", "New Bank", 2000m, 25, 10));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var context = CreateContext();
        var stored = await context.CreditCards.SingleAsync(item => item.PublicId == secondary.Id);
        Assert.Equal("Secondary", stored.Name);
        Assert.Equal("Example Bank", stored.Issuer);
        Assert.Equal(1000m, stored.CreditLimit);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateCreditCardCommand");
        Assert.Equal(CreditCardMessages.DuplicateName, audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignCard_WhenUpdated_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var deleted = await CreateCardAsync(owner, "Deleted");
        var foreign = await CreateCardAsync(owner, "Foreign");
        await using (var context = CreateContext())
        {
            var stored = await context.CreditCards.SingleAsync(item => item.PublicId == deleted.Id);
            stored.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var deletedResponse = await owner.PutAsJsonAsync(
            $"/api/credit-cards/{deleted.Id}", UpdateBody("Changed"));
        var foreignResponse = await other.PutAsJsonAsync(
            $"/api/credit-cards/{foreign.Id}", UpdateBody("Changed"));

        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains(CreditCardMessages.NotFound, await deletedResponse.Content.ReadAsStringAsync());
        Assert.Contains(CreditCardMessages.NotFound, await foreignResponse.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenInvalidLimitAndDays_WhenUpdated_ThenBadRequestStoresNoChanges()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateCardAsync(client, "Valid");

        var response = await client.PutAsJsonAsync(
            $"/api/credit-cards/{original.Id}",
            UpdateBody("Valid", "Example Bank", 0m, 0, 32));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CreditCardMessages.CreditLimitPositive, body, StringComparison.Ordinal);
        Assert.Contains(CreditCardMessages.ClosingDayInvalid, body, StringComparison.Ordinal);
        Assert.Contains(CreditCardMessages.DueDayInvalid, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True(await context.CreditCards.AnyAsync(item =>
            item.PublicId == original.Id && item.CreditLimit == 1000m));
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenUpdated_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/credit-cards/{Guid.NewGuid()}", UpdateBody("Changed"));
        var administratorResponse = await administrator.PutAsJsonAsync(
            $"/api/credit-cards/{Guid.NewGuid()}", UpdateBody("Changed"));

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

    private static async Task<CreditCardData> CreateCardAsync(
        HttpClient client,
        string name,
        string issuer = "Example Bank",
        string currencyCode = "BRL",
        decimal creditLimit = 1000m,
        short closingDay = 20,
        short dueDay = 5)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = issuer,
            CurrencyCode = currencyCode,
            CreditLimit = creditLimit,
            ClosingDay = closingDay,
            DueDay = dueDay,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreditCardEnvelope>())!.Data!;
    }

    private static object UpdateBody(
        string name,
        string issuer = "Example Bank",
        decimal creditLimit = 1000m,
        short closingDay = 20,
        short dueDay = 5) => new
        {
            Name = name,
            Issuer = issuer,
            CreditLimit = creditLimit,
            ClosingDay = closingDay,
            DueDay = dueDay
        };

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
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

    private sealed record CreditCardEnvelope(
        CreditCardData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record CreditCardData(
        Guid Id,
        string Name,
        string Issuer,
        string CurrencyCode,
        decimal CreditLimit,
        short ClosingDay,
        short DueDay,
        string? LastFourDigits,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
