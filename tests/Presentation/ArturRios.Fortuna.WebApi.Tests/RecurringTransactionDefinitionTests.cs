using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
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

public sealed class RecurringTransactionDefinitionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenMonthlyRule_WhenDefined_ThenPreviewClampsAndCreatesNoMovement()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Monthly account");
        var category = await SeedCategoryAsync(subject, "Rent");
        var startsOn = new DateOnly(Today.Year + 1, 1, 31);

        var response = await client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = account,
            CategoryId = category,
            Direction = TransactionDirection.Expense,
            Amount = 100m,
            Frequency = RecurrenceFrequency.Monthly,
            StartsOn = startsOn,
            Description = "Monthly rent",
            Counterparty = "Landlord"
        });
        var rule = (await response.Content.ReadFromJsonAsync<RuleEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            [startsOn, startsOn.AddMonths(1), startsOn.AddMonths(2), startsOn.AddMonths(3), startsOn.AddMonths(4)],
            rule.NextOccurrences);
        Assert.Equal("Landlord", rule.CounterpartyName);
        Assert.Equal("BRL", rule.CurrencyCode);
        await using var context = CreateContext();
        Assert.Single(await context.RecurringTransactions.ToArrayAsync());
        Assert.Empty(await context.FinancialTransactions.ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenPastBoundedCardRule_WhenDefinedAndRead_ThenOnlyUpcomingDatesReturn()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Salary card");
        var category = await SeedCategoryAsync(subject, "Income");
        var startsOn = Today.AddMonths(-2);
        var endsOn = Today.AddMonths(2);

        var create = await client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            CreditCardId = card,
            CategoryId = category,
            Direction = TransactionDirection.Earning,
            Amount = 25m,
            Frequency = RecurrenceFrequency.Monthly,
            StartsOn = startsOn,
            EndsOn = endsOn
        });
        var created = (await create.Content.ReadFromJsonAsync<RuleEnvelope>())!.Data!;
        var read = await client.GetFromJsonAsync<RuleEnvelope>(
            $"/api/recurring-transactions/{created.Id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(card, created.CreditCardId);
        Assert.Equal([Today, Today.AddMonths(1), Today.AddMonths(2)], created.NextOccurrences);
        Assert.Equal(created.NextOccurrences, read?.Data?.NextOccurrences);
        Assert.Null(read?.Data?.LastMaterializedOn);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrForeignDefinition_WhenPosted_ThenItIsRejectedWithoutRule()
    {
        var ownerSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(owner, "Owner profile account");
        var foreignAccount = await CreateAccountAsync(other, "Foreign account");
        var category = await SeedCategoryAsync(ownerSubject, "Private");

        var invalid = await owner.PostAsJsonAsync("/api/recurring-transactions", new
        {
            Amount = 0m,
            Frequency = (RecurrenceFrequency)99,
            StartsOn = Today,
            EndsOn = Today.AddDays(-1)
        });
        var foreign = await owner.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = foreignAccount,
            CategoryId = category,
            Direction = TransactionDirection.Expense,
            Amount = 10m,
            Frequency = RecurrenceFrequency.Weekly,
            StartsOn = Today
        });
        var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/recurring-transactions",
            new { FinancialAccountId = foreignAccount });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(RecurringTransactionMessages.DateRangeInvalid,
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await using var context = CreateContext();
        Assert.Empty(await context.RecurringTransactions.ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignRuleIdentifier_WhenRead_ThenOwnershipIsHidden()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Private rule account");
        var category = await SeedCategoryAsync(subject, "Private rule");
        var create = await owner.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = account,
            CategoryId = category,
            Direction = TransactionDirection.Expense,
            Amount = 10m,
            Frequency = RecurrenceFrequency.Weekly,
            StartsOn = Today
        });
        var rule = (await create.Content.ReadFromJsonAsync<RuleEnvelope>())!.Data!;

        var response = await other.GetAsync($"/api/recurring-transactions/{rule.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var category = new Category(user, name, DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.PublicId;
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

    private static async Task<Guid> CreateCardAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 25
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
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options,
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        DatabaseDiagnosticsOptions.Disabled);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), []) { DisplayName = "Rule Owner" };
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

    private sealed record RuleEnvelope(RuleData? Data);
    private sealed record RuleData(
        Guid Id,
        Guid? CreditCardId,
        string CurrencyCode,
        DateOnly? LastMaterializedOn,
        string? CounterpartyName,
        IReadOnlyCollection<DateOnly> NextOccurrences);
    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
}
