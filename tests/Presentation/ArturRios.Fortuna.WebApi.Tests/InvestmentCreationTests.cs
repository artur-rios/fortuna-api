using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Investments;
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

public sealed class InvestmentCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenValidInvestment_WhenCreated_ThenOwnedInvestmentAndAuditAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/investments", Command(
            "  Treasury Bond  ",
            "  Example Broker  ",
            InvestmentType.FixedIncome,
            "brl"));
        var envelope = await response.Content.ReadFromJsonAsync<InvestmentEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(envelope?.Data);
        Assert.NotEqual(Guid.Empty, envelope.Data.Id);
        Assert.Equal("Treasury Bond", envelope.Data.Instrument);
        Assert.Equal("Example Broker", envelope.Data.Institution);
        Assert.Equal(InvestmentType.FixedIncome, envelope.Data.InvestmentType);
        Assert.Equal("BRL", envelope.Data.CurrencyCode);
        Assert.Equal(envelope.Data.CreatedAt, envelope.Data.UpdatedAt);
        await using var context = CreateContext();
        var investment = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == envelope.Data.Id);
        Assert.Equal(subject.ToString("D"), investment.User.ExternalSubject);
        Assert.Equal("TREASURY BOND", investment.NormalizedInstrument);
        Assert.Equal("BRL", investment.Currency.Code);
        Assert.False(investment.IsDeleted);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "CreateInvestmentCommand");
        Assert.Equal(investment.User.PublicId, audit.ActorUserId);
        Assert.Equal("Investment", audit.EntityType);
        Assert.Equal(investment.PublicId, audit.EntityPublicId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Null(audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveInstrument_WhenCreated_ThenConflictIsAudited()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var first = await client.PostAsJsonAsync("/api/investments", Command(
            "Index Fund", null, InvestmentType.Fund, "BRL"));
        var duplicate = await client.PostAsJsonAsync("/api/investments", Command(
            "  index fund  ", "Other Broker", InvestmentType.Equity, "USD"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Investments.CountAsync());
        var audits = await context.AuditEntries
            .Where(item => item.Operation == "CreateInvestmentCommand")
            .OrderBy(item => item.OccurredAt)
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.Contains(audits, item => item.Outcome == AuditOutcome.Succeeded);
        var refused = Assert.Single(audits, item => item.Outcome == AuditOutcome.Refused);
        Assert.Equal(InvestmentMessages.DuplicateInstrument, refused.Reason);
    }

    [FunctionalFact]
    public async Task GivenDuplicateSoftDeletedInstrument_WhenCreated_ThenItIsAllowed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var firstResponse = await client.PostAsJsonAsync("/api/investments", Command(
            "Retirement", null, InvestmentType.Fund, "USD"));
        var first = (await firstResponse.Content.ReadFromJsonAsync<InvestmentEnvelope>())!.Data!;
        await using (var context = CreateContext())
        {
            var investment = await context.Investments.SingleAsync(item =>
                item.PublicId == first.Id);
            investment.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var replacement = await client.PostAsJsonAsync("/api/investments", Command(
            "retirement", null, InvestmentType.Fund, "USD"));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        await using var assertionContext = CreateContext();
        var investments = await assertionContext.Investments
            .Where(item => item.NormalizedInstrument == "RETIREMENT")
            .OrderBy(item => item.IsDeleted)
            .ToArrayAsync();
        Assert.Equal(2, investments.Length);
        Assert.Single(investments, item => item.IsDeleted);
        Assert.Single(investments, item => !item.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenSameInstrumentForDifferentUsers_WhenCreated_ThenBothAreAllowed()
    {
        await using var factory = CreateFactory();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(secondClient, Guid.NewGuid(), HeimdallRoles.User);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/investments", Command(
                "Shared Fund", null, InvestmentType.Fund, "BRL")),
            secondClient.PostAsJsonAsync("/api/investments", Command(
                "Shared Fund", null, InvestmentType.Fund, "BRL")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var context = CreateContext();
        Assert.Equal(2, await context.Investments.CountAsync());
        Assert.Equal(2, await context.Investments.Select(item => item.UserId).Distinct().CountAsync());
    }

    [FunctionalFact]
    public async Task GivenConcurrentDuplicateInstruments_WhenCreated_ThenExactlyOneWins()
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
            firstClient.PostAsJsonAsync("/api/investments", Command(
                "Concurrent Fund", null, InvestmentType.Fund, "BRL")),
            secondClient.PostAsJsonAsync("/api/investments", Command(
                "concurrent fund", null, InvestmentType.Equity, "USD")));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        await using var context = CreateContext();
        Assert.Equal(1, await context.Investments.CountAsync());
        Assert.Equal(2, await context.AuditEntries.CountAsync(item =>
            item.Operation == "CreateInvestmentCommand"));
    }

    [FunctionalFact]
    public async Task GivenMissingRequiredFields_WhenCreated_ThenBadRequestNamesFields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/investments", new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(InvestmentMessages.InstrumentRequired, body, StringComparison.Ordinal);
        Assert.Contains(InvestmentMessages.InvestmentTypeInvalid, body, StringComparison.Ordinal);
        Assert.Contains(InvestmentMessages.CurrencyRequired, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Investments.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenUnknownCurrency_WhenCreated_ThenBadRequestNamesCurrency()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/investments", Command(
            "Unknown Currency", null, InvestmentType.Other, "ZZZ"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(InvestmentMessages.UnknownCurrency("ZZZ"), body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Investments.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenCreated_ThenNothingIsStored()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var command = Command("Hidden", null, InvestmentType.Other, "BRL");

        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/investments", command);
        var administratorResponse = await administrator.PostAsJsonAsync(
            "/api/investments",
            command);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Investments.AnyAsync());
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
        string instrument,
        string? institution,
        InvestmentType investmentType,
        string currencyCode) => new
        {
            Instrument = instrument,
            Institution = institution,
            InvestmentType = investmentType,
            CurrencyCode = currencyCode
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
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
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

    private sealed record InvestmentEnvelope(InvestmentData? Data);

    private sealed record InvestmentData(
        Guid Id,
        string Instrument,
        string? Institution,
        InvestmentType InvestmentType,
        string CurrencyCode,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
