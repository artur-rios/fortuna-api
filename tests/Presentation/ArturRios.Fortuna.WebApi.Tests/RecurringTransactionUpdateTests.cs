using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
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

public sealed class RecurringTransactionUpdateTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MutableTimeProvider clock = new(Now);

    [FunctionalFact]
    public async Task GivenMaterializedRule_WhenUpdated_ThenOnlyLaterOccurrencesUseNewTemplate()
    {
        clock.Set(Now);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Forward account");
        var oldCategory = await SeedCategoryAsync(subject, "Old category");
        var newCategory = await SeedCategoryAsync(subject, "New category");
        var rule = await DefineRuleAsync(
            client, account, oldCategory, Today.AddDays(-7), RecurrenceFrequency.Weekly);
        Assert.Equal(2, (await MaterializeAsync(client)).Data?.CreatedCount);

        var update = await client.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}",
            UpdateBody(account, newCategory, new DateOnly(2026, 8, 10), null));
        var updated = (await update.Content.ReadFromJsonAsync<UpdateEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(75m, updated.Amount);
        Assert.Equal(new DateOnly(2026, 9, 10), updated.AppliesFrom);
        Assert.False(updated.MaterializedOccurrencesChanged);
        clock.Set(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, (await MaterializeAsync(client)).Data?.CreatedCount);

        await using var context = CreateContext();
        var occurrences = await context.FinancialTransactions
            .Where(item => item.RecurringTransaction!.PublicId == rule)
            .Include(item => item.Category)
            .OrderBy(item => item.OccurredOn)
            .ToArrayAsync();
        Assert.Equal(4, occurrences.Length);
        Assert.All(occurrences[..2], item =>
        {
            Assert.Equal(50m, item.Amount);
            Assert.Equal("Old category", item.Category.Name);
        });
        Assert.All(occurrences[2..], item =>
        {
            Assert.Equal(75m, item.Amount);
            Assert.Equal("New category", item.Category.Name);
            Assert.Equal("Updated commitment", item.Description);
        });
    }

    [FunctionalFact]
    public async Task GivenEndBeforeLastMaterialized_WhenUpdated_ThenRuleStopsWithoutRemovingPast()
    {
        clock.Set(Now);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Ended account");
        var category = await SeedCategoryAsync(subject, "Ended category");
        var startsOn = new DateOnly(2026, 7, 5);
        var rule = await DefineRuleAsync(
            client, account, category, startsOn, RecurrenceFrequency.Monthly);
        Assert.Equal(3, (await MaterializeAsync(client)).Data?.CreatedCount);

        var update = await client.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}",
            UpdateBody(account, category, startsOn, new DateOnly(2026, 8, 5)));
        var updated = (await update.Content.ReadFromJsonAsync<UpdateEnvelope>())!.Data!;
        var rerun = await MaterializeAsync(client);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(new DateOnly(2026, 9, 5), updated.LastMaterializedOn);
        Assert.Null(updated.AppliesFrom);
        Assert.Empty(updated.NextOccurrences);
        Assert.Equal(0, rerun.Data?.CreatedCount);
        Assert.True(Assert.Single(rerun.Data!.Rules, item => item.RuleId == rule).IsComplete);
        await using var context = CreateContext();
        Assert.Equal(3, await context.FinancialTransactions.CountAsync(item =>
            item.RecurringTransaction!.PublicId == rule));
    }

    [FunctionalFact]
    public async Task GivenMaterializedRule_WhenDeleted_ThenOnlyRuleStopsProducing()
    {
        clock.Set(Now);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Delete account");
        var category = await SeedCategoryAsync(subject, "Delete category");
        var rule = await DefineRuleAsync(
            client, account, category, Today, RecurrenceFrequency.Monthly);
        Assert.Equal(1, (await MaterializeAsync(client)).Data?.CreatedCount);

        var deletion = await client.DeleteAsync($"/api/recurring-transactions/{rule}");
        var deleted = (await deletion.Content.ReadFromJsonAsync<DeleteEnvelope>())!.Data!;
        clock.Set(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var rerun = await MaterializeAsync(client);
        var read = await client.GetAsync($"/api/recurring-transactions/{rule}");
        var update = await client.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}",
            UpdateBody(account, category, Today, null));

        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.False(deleted.MaterializedOccurrencesChanged);
        Assert.Equal(0, rerun.Data?.CreatedCount);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        await using var context = CreateContext();
        Assert.True((await context.RecurringTransactions.SingleAsync(item =>
            item.PublicId == rule)).IsDeleted);
        Assert.Single(await context.FinancialTransactions.Where(item =>
            item.RecurringTransaction!.PublicId == rule).ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenInvalidForeignOrAnonymousUpdate_WhenRequested_ThenRuleRemainsPrivateAndUnchanged()
    {
        clock.Set(Now);
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Private update");
        var category = await SeedCategoryAsync(subject, "Private category");
        var rule = await DefineRuleAsync(
            owner, account, category, Today, RecurrenceFrequency.Monthly);
        await CreateAccountAsync(other, "Other profile");
        var invalidBody = UpdateBody(account, category, Today.AddDays(1), Today);

        var invalid = await owner.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}", invalidBody);
        var foreignUpdate = await other.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}", UpdateBody(account, category, Today, null));
        var foreignDelete = await other.DeleteAsync($"/api/recurring-transactions/{rule}");
        var anonymousUpdate = await anonymous.PutAsJsonAsync(
            $"/api/recurring-transactions/{rule}", UpdateBody(account, category, Today, null));
        var anonymousDelete = await anonymous.DeleteAsync($"/api/recurring-transactions/{rule}");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDelete.StatusCode);
        await using var context = CreateContext();
        var saved = await context.RecurringTransactions.SingleAsync(item => item.PublicId == rule);
        Assert.False(saved.IsDeleted);
        Assert.Equal(50m, saved.Amount);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> SeedCategoryAsync(Guid subject, string name)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var category = new Category(user, name, clock.GetUtcNow());
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.PublicId;
    }

    private static async Task<Guid> DefineRuleAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        DateOnly startsOn,
        RecurrenceFrequency frequency)
    {
        var response = await client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Direction = TransactionDirection.Expense,
            Amount = 50m,
            Frequency = frequency,
            StartsOn = startsOn,
            Description = "Original commitment"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static object UpdateBody(
        Guid accountId,
        Guid categoryId,
        DateOnly startsOn,
        DateOnly? endsOn) => new
        {
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Direction = TransactionDirection.Expense,
            Amount = 75m,
            Frequency = RecurrenceFrequency.Monthly,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Description = "Updated commitment",
            Counterparty = "Updated counterparty"
        };

    private static async Task<MaterializationEnvelope> MaterializeAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/recurring-transactions/materialize", new { });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterializationEnvelope>())!;
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = "Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = "BRL",
            OpeningBalance = 1000m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        foreach (var setting in ValidSettings()) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options,
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        DatabaseDiagnosticsOptions.Disabled);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Recurring Rule Owner"
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

    private sealed record UpdateEnvelope(UpdateData? Data);
    private sealed record UpdateData(
        decimal Amount,
        DateOnly? LastMaterializedOn,
        DateOnly? AppliesFrom,
        bool MaterializedOccurrencesChanged,
        IReadOnlyCollection<DateOnly> NextOccurrences);
    private sealed record DeleteEnvelope(DeleteData? Data);
    private sealed record DeleteData(bool MaterializedOccurrencesChanged);
    private sealed record MaterializationEnvelope(MaterializationData? Data);
    private sealed record MaterializationData(int CreatedCount, IReadOnlyCollection<RuleReport> Rules);
    private sealed record RuleReport(Guid RuleId, bool IsComplete);
    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Set(DateTimeOffset value) => now = value;
    }
}
