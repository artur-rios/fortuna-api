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

public sealed class InvestmentLifecycleTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenChildren_WhenDeletedAndRestored_ThenOnlyCascadeMembersReturn()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var id = await SeedInvestmentAsync(client, subject, "Lifecycle");
        var children = await SeedChildrenAsync(id);

        var deleted = await client.DeleteAsync($"/api/investments/{id}");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using (var context = CreateContext())
        {
            var investment = await context.Investments.SingleAsync(item => item.PublicId == id);
            var movements = await context.InvestmentMovements
                .Where(item => item.InvestmentId == investment.Id)
                .ToArrayAsync();
            var valuations = await context.InvestmentValuations
                .Where(item => item.InvestmentId == investment.Id)
                .ToArrayAsync();
            Assert.True(investment.IsDeleted);
            Assert.All(movements, item => Assert.True(item.IsDeleted));
            Assert.All(valuations, item => Assert.True(item.IsDeleted));
            Assert.Equal(investment.DeletionCascadeId, movements.Single(item =>
                item.PublicId == children.LiveMovementId).DeletionCascadeId);
            Assert.NotEqual(investment.DeletionCascadeId, movements.Single(item =>
                item.PublicId == children.PredeletedMovementId).DeletionCascadeId);
            Assert.Equal(investment.DeletionCascadeId, valuations.Single(item =>
                item.PublicId == children.LiveValuationId).DeletionCascadeId);
            Assert.NotEqual(investment.DeletionCascadeId, valuations.Single(item =>
                item.PublicId == children.PredeletedValuationId).DeletionCascadeId);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/investments/{id}")).StatusCode);
        var restored = await client.PostAsync($"/api/investments/{id}/restore", null);

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await using var assertionContext = CreateContext();
        var restoredInvestment = await assertionContext.Investments.SingleAsync(item =>
            item.PublicId == id);
        var restoredMovements = await assertionContext.InvestmentMovements
            .Where(item => item.InvestmentId == restoredInvestment.Id)
            .ToArrayAsync();
        var restoredValuations = await assertionContext.InvestmentValuations
            .Where(item => item.InvestmentId == restoredInvestment.Id)
            .ToArrayAsync();
        Assert.False(restoredInvestment.IsDeleted);
        Assert.False(restoredMovements.Single(item =>
            item.PublicId == children.LiveMovementId).IsDeleted);
        Assert.True(restoredMovements.Single(item =>
            item.PublicId == children.PredeletedMovementId).IsDeleted);
        Assert.False(restoredValuations.Single(item =>
            item.PublicId == children.LiveValuationId).IsDeleted);
        Assert.True(restoredValuations.Single(item =>
            item.PublicId == children.PredeletedValuationId).IsDeleted);
        var audits = await assertionContext.AuditEntries.Where(item =>
            item.Operation == "DeleteInvestmentCommand" ||
            item.Operation == "RestoreInvestmentCommand").ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, item => Assert.Equal(AuditOutcome.Succeeded, item.Outcome));
    }

    [FunctionalFact]
    public async Task GivenLiveInvestment_WhenHardDeleted_ThenConflictLeavesItIntact()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var id = await SeedInvestmentAsync(client, subject, "Live");

        var response = await client.DeleteAsync($"/api/investments/{id}/hard");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.HardDeleteRequiresSoftDeletion,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True(await context.Investments.AnyAsync(item => item.PublicId == id));
    }

    [FunctionalFact]
    public async Task GivenSoftDeletedInvestment_WhenHardDeleted_ThenChildrenAndParentAreRemoved()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var id = await SeedInvestmentAsync(client, subject, "Permanent");
        await SeedChildrenAsync(id);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/investments/{id}")).StatusCode);

        var response = await client.DeleteAsync($"/api/investments/{id}/hard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Investments.AnyAsync(item => item.PublicId == id));
        Assert.False(await context.InvestmentMovements.AnyAsync(item =>
            item.Investment.PublicId == id));
        Assert.False(await context.InvestmentValuations.AnyAsync(item =>
            item.Investment.PublicId == id));
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveInstrument_WhenRestored_ThenConflictKeepsItDeleted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var archivedId = await SeedInvestmentAsync(client, subject, "Duplicate");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.DeleteAsync($"/api/investments/{archivedId}")).StatusCode);
        await SeedInvestmentAsync(client, subject, "Duplicate");

        var response = await client.PostAsync($"/api/investments/{archivedId}/restore", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.DuplicateInstrument,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True((await context.Investments.SingleAsync(item =>
            item.PublicId == archivedId)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenLiveInvestment_WhenRestored_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var id = await SeedInvestmentAsync(client, subject, "Live Restore");

        var response = await client.PostAsync($"/api/investments/{id}/restore", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            InvestmentMessages.RestoreRequiresSoftDeletion,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenMissingOrForeignInvestment_WhenLifecycleRequested_ThenNotFoundIsReturned()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        var id = await SeedInvestmentAsync(owner, ownerSubject, "Private");
        await EnsureProfileAsync(other);

        var foreign = await other.DeleteAsync($"/api/investments/{id}");
        var missing = await other.DeleteAsync($"/api/investments/{Guid.NewGuid()}");
        var foreignRestore = await other.PostAsync($"/api/investments/{id}/restore", null);
        var foreignHardDelete = await other.DeleteAsync($"/api/investments/{id}/hard");

        Assert.All([foreign, missing, foreignRestore, foreignHardDelete], response =>
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenLifecycleRequested_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var anonymousDelete = await anonymous.DeleteAsync($"/api/investments/{id}");
        var administratorDelete = await administrator.DeleteAsync($"/api/investments/{id}");
        var anonymousRestore = await anonymous.PostAsync($"/api/investments/{id}/restore", null);
        var administratorHardDelete = await administrator.DeleteAsync($"/api/investments/{id}/hard");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRestore.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorHardDelete.StatusCode);
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
        string instrument)
    {
        await EnsureProfileAsync(client);
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var investment = new Investment(
            user,
            instrument,
            "Broker",
            InvestmentType.Fund,
            currency,
            DateTimeOffset.UtcNow);
        context.Investments.Add(investment);
        await context.SaveChangesAsync();
        return investment.PublicId;
    }

    private async Task<SeededChildren> SeedChildrenAsync(Guid investmentId)
    {
        await using var context = CreateContext();
        var investment = await context.Investments.SingleAsync(item => item.PublicId == investmentId);
        var liveMovement = new InvestmentMovement(
            investment,
            InvestmentMovementType.Contribution,
            10m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        var predeletedMovement = new InvestmentMovement(
            investment,
            InvestmentMovementType.Fee,
            1m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        predeletedMovement.SoftDelete(DateTimeOffset.UtcNow);
        var liveValuation = new InvestmentValuation(
            investment,
            10m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        var predeletedValuation = new InvestmentValuation(
            investment,
            8m,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            DateTimeOffset.UtcNow);
        predeletedValuation.SoftDelete(DateTimeOffset.UtcNow);
        context.InvestmentMovements.AddRange(liveMovement, predeletedMovement);
        context.InvestmentValuations.AddRange(liveValuation, predeletedValuation);
        await context.SaveChangesAsync();
        return new SeededChildren(
            liveMovement.PublicId,
            predeletedMovement.PublicId,
            liveValuation.PublicId,
            predeletedValuation.PublicId);
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

    private sealed record SeededChildren(
        Guid LiveMovementId,
        Guid PredeletedMovementId,
        Guid LiveValuationId,
        Guid PredeletedValuationId);
}
