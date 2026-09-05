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

public sealed class InvestmentValuationRecordingTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenMovements_WhenValued_ThenLatestBaselineAndLaterMovementsFormPosition()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        await SeedMovementsAsync(
            investmentId,
            (InvestmentMovementType.Contribution, 100m, Today().AddDays(-3)),
            (InvestmentMovementType.Yield, 10m, Today().AddDays(-2)),
            (InvestmentMovementType.Fee, 4m, Today().AddDays(-1)));

        var envelope = await PostAsync(client, investmentId, 120m, Today().AddDays(-2));

        Assert.Equal(120m, envelope.Data!.Value);
        Assert.Equal("BRL", envelope.Data.CurrencyCode);
        Assert.Equal(116m, envelope.Data.Position);
        Assert.True(envelope.Data.IsIndependentlyValued);
        Assert.Equal(120m, envelope.Data.LatestValuationValue);
        Assert.Equal(Today().AddDays(-2), envelope.Data.LatestValuationDate);
        Assert.False(envelope.Data.ReplacedExisting);
        await using var context = CreateContext();
        var valuation = await context.InvestmentValuations
            .Include(item => item.Investment)
            .SingleAsync(item => item.PublicId == envelope.Data.Id);
        Assert.Equal(investmentId, valuation.Investment.PublicId);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "RecordInvestmentValuationCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("InvestmentValuation", audit.EntityType);
        Assert.Equal(valuation.PublicId, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenExistingDate_WhenValued_ThenSameRecordIsReplacedAndAudited()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        var valuedOn = Today().AddDays(-1);

        var first = await PostAsync(client, investmentId, 100m, valuedOn);
        var replacement = await PostAsync(client, investmentId, 125m, valuedOn);

        Assert.Equal(first.Data!.Id, replacement.Data!.Id);
        Assert.False(first.Data.ReplacedExisting);
        Assert.True(replacement.Data.ReplacedExisting);
        Assert.Equal(125m, replacement.Data.Value);
        Assert.InRange(
            (first.Data.CreatedAt - replacement.Data.CreatedAt).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMicroseconds(1));
        Assert.True(replacement.Data.UpdatedAt >= first.Data.UpdatedAt);
        await using var context = CreateContext();
        var stored = await context.InvestmentValuations.Where(item =>
            item.Investment.PublicId == investmentId && item.ValuedOn == valuedOn).ToArrayAsync();
        Assert.Single(stored);
        Assert.Equal(125m, stored[0].Value);
        Assert.Equal(2, await context.AuditEntries.CountAsync(item =>
            item.Operation == "RecordInvestmentValuationCommand" &&
            item.EntityPublicId == stored[0].PublicId &&
            item.Outcome == AuditOutcome.Succeeded));
    }

    [FunctionalFact]
    public async Task GivenConcurrentSameDate_WhenValued_ThenOneRecordIsCreatedAndReplaced()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var provisioningClient = factory.CreateClient();
        Authorize(provisioningClient, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(
            provisioningClient, subject, "BRL");
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, subject, HeimdallRoles.User);
        Authorize(secondClient, subject, HeimdallRoles.User);

        var valuations = await Task.WhenAll(
            PostAsync(firstClient, investmentId, 100m, Today()),
            PostAsync(secondClient, investmentId, 125m, Today()));

        Assert.Single(valuations, item => !item.Data!.ReplacedExisting);
        Assert.Single(valuations, item => item.Data!.ReplacedExisting);
        Assert.Equal(valuations[0].Data!.Id, valuations[1].Data!.Id);
        await using var context = CreateContext();
        Assert.Equal(1, await context.InvestmentValuations.CountAsync(item =>
            item.Investment.PublicId == investmentId && item.ValuedOn == Today()));
    }

    [FunctionalFact]
    public async Task GivenNegativeValue_WhenValued_ThenExactPositionIsAccepted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "USD");

        var envelope = await PostAsync(client, investmentId, -50.25m, Today());

        Assert.Equal(-50.25m, envelope.Data!.Value);
        Assert.Equal(-50.25m, envelope.Data.Position);
        Assert.Equal("USD", envelope.Data.CurrencyCode);
    }

    [FunctionalFact]
    public async Task GivenOlderValuation_WhenRecorded_ThenExistingLatestValuationStillDrivesPosition()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");
        await PostAsync(client, investmentId, 200m, Today());

        var historical = await PostAsync(
            client, investmentId, 100m, Today().AddDays(-1));

        Assert.Equal(100m, historical.Data!.Value);
        Assert.Equal(200m, historical.Data.Position);
        Assert.Equal(200m, historical.Data.LatestValuationValue);
        Assert.Equal(Today(), historical.Data.LatestValuationDate);
    }

    [FunctionalFact]
    public async Task GivenFutureDate_WhenValued_ThenBadRequestStoresNothing()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(client, subject, "BRL");

        var response = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/valuations",
            Command(100m, Today().AddDays(1)));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(InvestmentMessages.ValuedOnFuture, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentValuations.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "RecordInvestmentValuationCommand");
        Assert.Equal(AuditOutcome.Refused, audit.Outcome);
    }

    [FunctionalFact]
    public async Task GivenDeletedOrForeignInvestment_WhenValued_ThenNotFoundIsReturned()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        using var otherClient = factory.CreateClient();
        Authorize(ownerClient, owner, HeimdallRoles.User);
        Authorize(otherClient, other, HeimdallRoles.User);
        var deletedId = await SeedInvestmentAsync(ownerClient, owner, "BRL", true);
        var liveId = await SeedInvestmentAsync(ownerClient, owner, "BRL");
        await EnsureProfileAsync(otherClient);

        var deleted = await ownerClient.PostAsJsonAsync(
            $"/api/investments/{deletedId}/valuations",
            Command(100m, Today()));
        var foreign = await otherClient.PostAsJsonAsync(
            $"/api/investments/{liveId}/valuations",
            Command(100m, Today()));

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentValuations.AnyAsync(item =>
            item.Investment.PublicId == deletedId || item.Investment.PublicId == liveId));
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenValued_ThenNothingIsStored()
    {
        var owner = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, owner, HeimdallRoles.User);
        var investmentId = await SeedInvestmentAsync(ownerClient, owner, "BRL");
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var command = Command(100m, Today());

        var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/investments/{investmentId}/valuations", command);
        var administratorResponse = await administrator.PostAsJsonAsync(
            $"/api/investments/{investmentId}/valuations", command);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.InvestmentValuations.AnyAsync(item =>
            item.Investment.PublicId == investmentId));
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> SeedInvestmentAsync(
        HttpClient client,
        Guid subject,
        string currencyCode,
        bool deleted = false)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == currencyCode);
        var investment = new Investment(
            user,
            $"Fund {Guid.NewGuid():N}",
            "Broker",
            InvestmentType.Fund,
            currency,
            DateTimeOffset.UtcNow);
        if (deleted)
        {
            investment.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.Investments.Add(investment);
        await context.SaveChangesAsync();
        return investment.PublicId;
    }

    private async Task SeedMovementsAsync(
        Guid investmentId,
        params (InvestmentMovementType Type, decimal Amount, DateOnly OccurredOn)[] records)
    {
        await using var context = CreateContext();
        var investment = await context.Investments.SingleAsync(item =>
            item.PublicId == investmentId);
        context.InvestmentMovements.AddRange(records.Select(record => new InvestmentMovement(
            investment,
            record.Type,
            record.Amount,
            record.OccurredOn,
            DateTimeOffset.UtcNow)));
        await context.SaveChangesAsync();
    }

    private static async Task EnsureProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<ValuationEnvelope> PostAsync(
        HttpClient client,
        Guid investmentId,
        decimal value,
        DateOnly valuedOn)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/investments/{investmentId}/valuations",
            Command(value, valuedOn));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return (await response.Content.ReadFromJsonAsync<ValuationEnvelope>())!;
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

    private static object Command(decimal value, DateOnly valuedOn) => new
    {
        Value = value,
        ValuedOn = valuedOn
    };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

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

    private sealed record ValuationEnvelope(ValuationData? Data);

    private sealed record ValuationData(
        Guid Id,
        Guid InvestmentId,
        decimal Value,
        string CurrencyCode,
        DateOnly ValuedOn,
        bool ReplacedExisting,
        decimal Position,
        bool IsIndependentlyValued,
        decimal? LatestValuationValue,
        DateOnly? LatestValuationDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
