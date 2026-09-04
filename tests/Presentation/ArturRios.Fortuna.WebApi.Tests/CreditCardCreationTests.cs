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

public sealed class CreditCardCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenFollowingMonthDueDay_WhenCreated_ThenOwnedCardAndAuditAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "  Rewards  ", "  Example Bank  ", "brl", 5000.1234m, 28, 5, "1234"));
        var envelope = await response.Content.ReadFromJsonAsync<CreditCardEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.NotEqual(Guid.Empty, envelope.Data.Id);
        Assert.Equal("Rewards", envelope.Data.Name);
        Assert.Equal("Example Bank", envelope.Data.Issuer);
        Assert.Equal("BRL", envelope.Data.CurrencyCode);
        Assert.Equal(5000.1234m, envelope.Data.CreditLimit);
        Assert.Equal((short)28, envelope.Data.ClosingDay);
        Assert.Equal((short)5, envelope.Data.DueDay);
        Assert.Equal("1234", envelope.Data.LastFourDigits);
        Assert.Equal(envelope.Data.CreatedAt, envelope.Data.UpdatedAt);
        await using var context = CreateContext();
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == envelope.Data.Id);
        Assert.Equal(subject.ToString("D"), card.User.ExternalSubject);
        Assert.Equal("REWARDS", card.NormalizedName);
        Assert.Equal("BRL", card.Currency.Code);
        Assert.False(card.IsDeleted);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "CreateCreditCardCommand");
        Assert.Equal(card.User.PublicId, audit.ActorUserId);
        Assert.Equal("CreditCard", audit.EntityType);
        Assert.Equal(card.PublicId, audit.EntityPublicId);
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

        var first = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Household", "Bank One", "BRL", 1000, 10, 20));
        var duplicate = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "  household  ", "Bank Two", "BRL", 2000, 15, 25));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(1, await context.CreditCards.CountAsync());
        var audits = await context.AuditEntries
            .Where(item => item.Operation == "CreateCreditCardCommand")
            .OrderBy(item => item.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.Contains(audits, item => item.Outcome == AuditOutcome.Succeeded);
        var refused = Assert.Single(audits, item => item.Outcome == AuditOutcome.Refused);
        Assert.Equal(CreditCardMessages.DuplicateName, refused.Reason);
    }

    [FunctionalFact]
    public async Task GivenDuplicateSoftDeletedName_WhenCreated_ThenNewCardIsAllowed()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var firstResponse = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Travel", "Example Bank", "USD", 1000, 20, 5));
        var first = (await firstResponse.Content.ReadFromJsonAsync<CreditCardEnvelope>())!.Data!;
        await using (var context = CreateContext())
        {
            var card = await context.CreditCards.SingleAsync(item => item.PublicId == first.Id);
            card.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var replacement = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "travel", "Example Bank", "USD", 1500, 20, 5));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        await using var assertionContext = CreateContext();
        var cards = await assertionContext.CreditCards
            .Where(item => item.NormalizedName == "TRAVEL")
            .OrderBy(item => item.IsDeleted)
            .ToArrayAsync();
        Assert.Equal(2, cards.Length);
        Assert.Single(cards, item => item.IsDeleted);
        Assert.Single(cards, item => !item.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenSameNameForDifferentUsers_WhenCreated_ThenBothCardsAreAllowed()
    {
        await using var factory = CreateFactory();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(secondClient, Guid.NewGuid(), HeimdallRoles.User);

        var results = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/credit-cards", Command(
                "Everyday", "Example Bank", "BRL", 1000, 10, 20)),
            secondClient.PostAsJsonAsync("/api/credit-cards", Command(
                "Everyday", "Example Bank", "BRL", 1000, 10, 20)));

        Assert.All(results, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var context = CreateContext();
        Assert.Equal(2, await context.CreditCards.CountAsync());
        Assert.Equal(2, await context.CreditCards.Select(item => item.UserId).Distinct().CountAsync());
    }

    [FunctionalTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GivenNonPositiveLimit_WhenCreated_ThenBadRequestIsReturned(decimal limit)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Invalid Limit", "Example Bank", "BRL", limit, 10, 20));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.CreditCards.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenInvalidBillingDays_WhenCreated_ThenBadRequestNamesFields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Invalid Days", "Example Bank", "BRL", 1000, 0, 32));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CreditCardMessages.ClosingDayInvalid, body, StringComparison.Ordinal);
        Assert.Contains(CreditCardMessages.DueDayInvalid, body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenInvalidDigitsAndCurrency_WhenCreated_ThenBadRequestIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var invalidDigits = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Invalid Digits", "Example Bank", "BRL", 1000, 10, 20, "12a4"));
        var unknownCurrency = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Unknown Currency", "Example Bank", "ZZZ", 1000, 10, 20));
        var currencyBody = await unknownCurrency.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalidDigits.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownCurrency.StatusCode);
        Assert.Contains("Unknown currency code 'ZZZ'.", currencyBody, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.CreditCards.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenCreated_ThenUnauthorizedStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Hidden", "Example Bank", "BRL", 1000, 10, 20));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.CreditCards.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenCreated_ThenForbiddenStoresNothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.PostAsJsonAsync("/api/credit-cards", Command(
            "Admin Card", "Example Bank", "BRL", 1000, 10, 20));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.CreditCards.AnyAsync());
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
        string issuer,
        string currencyCode,
        decimal creditLimit,
        short closingDay,
        short dueDay,
        string? lastFourDigits = null) => new
        {
            Name = name,
            Issuer = issuer,
            CurrencyCode = currencyCode,
            CreditLimit = creditLimit,
            ClosingDay = closingDay,
            DueDay = dueDay,
            LastFourDigits = lastFourDigits
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

    private sealed record CreditCardEnvelope(CreditCardData? Data);

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
