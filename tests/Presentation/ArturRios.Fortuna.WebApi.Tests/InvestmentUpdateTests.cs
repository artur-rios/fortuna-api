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

public sealed class InvestmentUpdateTests : IAsyncLifetime
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
        var original = await SeedInvestmentAsync(
            client,
            subject,
            "Before",
            "Old Broker",
            InvestmentType.Fund,
            "USD");

        var response = await client.PutAsJsonAsync($"/api/investments/{original.Id}", new
        {
            Instrument = "  After  ",
            Institution = "  New Broker  ",
            InvestmentType = InvestmentType.Equity
        });
        var envelope = await response.Content.ReadFromJsonAsync<InvestmentEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original.Id, envelope?.Data?.Id);
        Assert.Equal("After", envelope?.Data?.Instrument);
        Assert.Equal("New Broker", envelope?.Data?.Institution);
        Assert.Equal(InvestmentType.Equity, envelope?.Data?.InvestmentType);
        Assert.Equal("USD", envelope?.Data?.CurrencyCode);
        Assert.True((envelope!.Data!.CreatedAt - original.CreatedAt).Duration() <
            TimeSpan.FromMilliseconds(1));
        Assert.True(envelope?.Data?.UpdatedAt >= original.UpdatedAt);
        Assert.Contains(InvestmentMessages.UpdatedSuccessfully, envelope.Messages);
        await using var context = CreateContext();
        var stored = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == original.Id);
        Assert.Equal(subject.ToString("D"), stored.User.ExternalSubject);
        Assert.Equal("AFTER", stored.NormalizedInstrument);
        Assert.Equal("New Broker", stored.Institution);
        Assert.Equal(InvestmentType.Equity, stored.InvestmentType);
        Assert.Equal("USD", stored.Currency.Code);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateInvestmentCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("Investment", audit.EntityType);
        Assert.Equal(original.Id, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenCurrencyChange_WhenUpdated_ThenBadRequestLeavesCurrencyUnchanged()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var original = await SeedInvestmentAsync(client, subject, "Fixed", "Broker", currency: "BRL");

        var response = await client.PutAsJsonAsync($"/api/investments/{original.Id}", new
        {
            Instrument = "Changed",
            Institution = "Broker",
            InvestmentType = InvestmentType.Fund,
            CurrencyCode = "USD"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.CurrencyImmutable,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.Investments
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == original.Id);
        Assert.Equal("Fixed", stored.Instrument);
        Assert.Equal("BRL", stored.Currency.Code);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateInvestmentCommand");
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveInstrument_WhenUpdated_ThenConflictLeavesRecordUnchanged()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await SeedInvestmentAsync(client, subject, "Primary", "Broker");
        var secondary = await SeedInvestmentAsync(client, subject, "Secondary", null);

        var response = await client.PutAsJsonAsync(
            $"/api/investments/{secondary.Id}",
            Body(" primary ", "New Broker", InvestmentType.Equity));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var context = CreateContext();
        var stored = await context.Investments.SingleAsync(item => item.PublicId == secondary.Id);
        Assert.Equal("Secondary", stored.Instrument);
        Assert.Null(stored.Institution);
        Assert.Equal(InvestmentType.Fund, stored.InvestmentType);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateInvestmentCommand");
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
        Assert.Equal(InvestmentMessages.DuplicateInstrument, audit.Reason);
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignInvestment_WhenUpdated_ThenSameNotFoundIsReturned()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        var deleted = await SeedInvestmentAsync(owner, ownerSubject, "Deleted", "Broker", deleted: true);
        var foreign = await SeedInvestmentAsync(owner, ownerSubject, "Foreign", "Broker");
        await EnsureProfileAsync(other);

        var deletedResponse = await owner.PutAsJsonAsync(
            $"/api/investments/{deleted.Id}", Body("Changed"));
        var foreignResponse = await other.PutAsJsonAsync(
            $"/api/investments/{foreign.Id}", Body("Changed"));

        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains(
            InvestmentMessages.NotFound,
            await deletedResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains(
            InvestmentMessages.NotFound,
            await foreignResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenInvalidEditableFields_WhenUpdated_ThenBadRequestStoresNoChanges()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var original = await SeedInvestmentAsync(client, subject, "Valid", "Broker");

        var response = await client.PutAsJsonAsync($"/api/investments/{original.Id}", new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(InvestmentMessages.InstrumentRequired, body, StringComparison.Ordinal);
        Assert.Contains(InvestmentMessages.InvestmentTypeInvalid, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True(await context.Investments.AnyAsync(item =>
            item.PublicId == original.Id && item.Instrument == "Valid"));
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenUpdated_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/investments/{Guid.NewGuid()}", Body("Changed"));
        var administratorResponse = await administrator.PutAsJsonAsync(
            $"/api/investments/{Guid.NewGuid()}", Body("Changed"));

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

    private async Task<InvestmentData> SeedInvestmentAsync(
        HttpClient client,
        Guid subject,
        string instrument,
        string? institution,
        InvestmentType investmentType = InvestmentType.Fund,
        string currency = "BRL",
        bool deleted = false)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var storedCurrency = await context.Currencies.SingleAsync(item => item.Code == currency);
        var investment = new Investment(
            user,
            instrument,
            institution,
            investmentType,
            storedCurrency,
            DateTimeOffset.UtcNow);
        if (deleted)
        {
            investment.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.Investments.Add(investment);
        await context.SaveChangesAsync();
        return new InvestmentData(
            investment.PublicId,
            investment.Instrument,
            investment.Institution,
            investment.InvestmentType,
            currency,
            investment.CreatedAt,
            investment.UpdatedAt);
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

    private static object Body(
        string instrument,
        string? institution = null,
        InvestmentType investmentType = InvestmentType.Fund) => new
        {
            Instrument = instrument,
            Institution = institution,
            InvestmentType = investmentType
        };

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Investment Owner"
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

    private sealed record InvestmentEnvelope(
        InvestmentData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record InvestmentData(
        Guid Id,
        string Instrument,
        string? Institution,
        InvestmentType InvestmentType,
        string CurrencyCode,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
