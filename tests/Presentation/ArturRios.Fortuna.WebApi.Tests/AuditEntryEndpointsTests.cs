using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
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

public sealed class AuditEntryEndpointsTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedEntries_WhenFiltered_ThenOnlyMatchingEntryIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var actorId = await ProvisionAndResolveActorAsync(client, subject);
        var entityId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        await AddEntriesAsync(
            Entry(actorId, "DeleteAccountCommand", "Account", entityId,
                AuditOutcome.Refused, "Live transactions still reference this account.", occurredAt),
            Entry(actorId, "UpdateAccountCommand", "Account", entityId,
                AuditOutcome.Succeeded, null, occurredAt),
            Entry(Guid.NewGuid(), "DeleteAccountCommand", "Account", entityId,
                AuditOutcome.Refused, "Other user's entry.", occurredAt));
        var from = Uri.EscapeDataString(occurredAt.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(occurredAt.AddMinutes(1).ToString("O"));

        var response = await client.GetAsync(
            $"/api/audit-entries?entityType=account&entityId={entityId}" +
            $"&operation=deleteaccountcommand&outcome=Refused&from={from}&to={to}" +
            "&pageNumber=1&pageSize=10");
        var envelope = await response.Content.ReadFromJsonAsync<AuditPageEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, envelope!.TotalItems);
        var item = Assert.Single(envelope.Data!);
        Assert.Equal(actorId, item.ActorUserId);
        Assert.Equal("DeleteAccountCommand", item.Operation);
        Assert.Equal("Account", item.EntityType);
        Assert.Equal(entityId, item.EntityId);
        Assert.Equal(AuditOutcome.Refused, item.Outcome);
        Assert.Equal("Live transactions still reference this account.", item.Reason);
        Assert.Equal(occurredAt, item.OccurredAt);
    }

    [FunctionalFact]
    public async Task GivenAuditForHardDeletedRecord_WhenListed_ThenEntryStillExists()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var actorId = await ProvisionAndResolveActorAsync(client, subject);
        Guid targetId;
        await using (var context = CreateContext())
        {
            var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
            var target = new UserProfile(
                Guid.NewGuid(),
                "Hard Deleted Target",
                currency,
                DateTimeOffset.UtcNow);
            context.UserProfiles.Add(target);
            await context.SaveChangesAsync();
            targetId = target.PublicId;
            context.AuditEntries.Add(Entry(
                actorId,
                "HardDeleteUserProfileCommand",
                "UserProfile",
                targetId));
            target.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));
            await context.SaveChangesAsync();
            target.EnsureHardDeletionAllowed();
            context.UserProfiles.Remove(target);
            await context.SaveChangesAsync();
        }

        var envelope = await client.GetFromJsonAsync<AuditPageEnvelope>(
            $"/api/audit-entries?entityId={targetId}");

        var item = Assert.Single(envelope!.Data!);
        Assert.Equal("HardDeleteUserProfileCommand", item.Operation);
        Assert.Equal(targetId, item.EntityId);
        await using var assertionContext = CreateContext();
        Assert.False(await assertionContext.UserProfiles.AnyAsync(item => item.PublicId == targetId));
    }

    [FunctionalFact]
    public async Task GivenAnotherUsersEntries_WhenListed_ThenAnEmptyPageIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await ProvisionAndResolveActorAsync(client, subject);
        var otherTargetId = Guid.NewGuid();
        await AddEntriesAsync(Entry(
            Guid.NewGuid(),
            "UpdateAccountCommand",
            "Account",
            otherTargetId));

        var envelope = await client.GetFromJsonAsync<AuditPageEnvelope>(
            $"/api/audit-entries?entityId={otherTargetId}");

        Assert.NotNull(envelope?.Data);
        Assert.Empty(envelope.Data);
        Assert.Equal(0, envelope.TotalItems);
        Assert.Equal(0, envelope.TotalPages);
    }

    [FunctionalFact]
    public async Task GivenRefusedWrite_WhenListed_ThenSystemReasonIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var refused = await client.PostAsJsonAsync("/api/exchange-rates", new
        {
            BaseCurrencyCode = "USD",
            QuoteCurrencyCode = "BRL",
            Rate = 0,
            RateDate = new DateOnly(2026, 9, 4)
        });
        var envelope = await client.GetFromJsonAsync<AuditPageEnvelope>(
            "/api/audit-entries?operation=RecordManualExchangeRateCommand&outcome=Refused");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var item = Assert.Single(envelope!.Data!);
        Assert.Equal(AuditOutcome.Refused, item.Outcome);
        Assert.Equal("Rate must be greater than zero.", item.Reason);
    }

    [FunctionalTheory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task GivenMutationMethod_WhenAuditTrailIsTargeted_ThenMethodNotAllowedIsReturned(
        string method)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/audit-entries")
        {
            Content = JsonContent.Create(new { Reason = "caller-supplied" })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenInvalidPageAndPeriod_WhenListed_ThenBadRequestIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.GetAsync(
            "/api/audit-entries?pageNumber=0&pageSize=101" +
            "&from=2026-09-05T00%3A00%3A00Z&to=2026-09-04T00%3A00%3A00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number must be at least 1.", body, StringComparison.Ordinal);
        Assert.Contains("Page size must be between 1 and 100.", body, StringComparison.Ordinal);
        Assert.Contains("The period start must not be later than its end.", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenNoToken_WhenAuditTrailIsRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/audit-entries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenInstanceAdministrator_WhenAuditTrailIsRequested_ThenForbiddenIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var response = await client.GetAsync("/api/audit-entries");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> ProvisionAndResolveActorAsync(HttpClient client, Guid subject)
    {
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        return await context.UserProfiles
            .Where(profile => profile.ExternalSubject == subject.ToString("D"))
            .Select(profile => profile.PublicId)
            .SingleAsync();
    }

    private async Task AddEntriesAsync(params AuditEntry[] entries)
    {
        await using var context = CreateContext();
        context.AuditEntries.AddRange(entries);
        await context.SaveChangesAsync();
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

    private static AuditEntry Entry(
        Guid actorUserId,
        string operation,
        string? entityType = null,
        Guid? entityId = null,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        string? reason = null,
        DateTimeOffset? occurredAt = null) => new(
            actorUserId,
            operation,
            entityType,
            entityId,
            outcome,
            reason,
            occurredAt ?? DateTimeOffset.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Audit User"
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

    private sealed record AuditPageEnvelope(
        IReadOnlyCollection<AuditItem>? Data,
        int PageNumber,
        int PageSize,
        int TotalItems,
        int TotalPages);

    private sealed record AuditItem(
        Guid ActorUserId,
        string Operation,
        string? EntityType,
        Guid? EntityId,
        AuditOutcome Outcome,
        string? Reason,
        DateTimeOffset OccurredAt);
}
