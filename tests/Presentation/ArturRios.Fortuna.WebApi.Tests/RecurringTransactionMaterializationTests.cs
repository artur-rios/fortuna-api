using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
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

public sealed class RecurringTransactionMaterializationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenPastDueRule_WhenMaterializedTwice_ThenOccurrencesExistExactlyOnce()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Idempotent account");
        var category = await SeedCategoryAsync(subject, "Subscriptions");
        var rule = await DefineRuleAsync(
            client, account, null, category, Today.AddMonths(-2), null, "Subscription");

        var first = await MaterializeAsync(client);
        var second = await MaterializeAsync(client);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(3, first.Data?.CreatedCount);
        Assert.Equal(0, second.Data?.CreatedCount);
        await using var context = CreateContext();
        var savedRule = await context.RecurringTransactions.SingleAsync(item => item.PublicId == rule);
        var occurrences = await context.FinancialTransactions
            .Where(item => item.RecurringTransactionId == savedRule.Id)
            .OrderBy(item => item.OccurredOn)
            .ToArrayAsync();
        Assert.Equal(3, occurrences.Length);
        Assert.Equal(Today, savedRule.LastMaterializedOn);
    }

    [FunctionalFact]
    public async Task GivenEndedCardRule_WhenMaterialized_ThenItIsCompleteAndAssignedToStatements()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Recurring card");
        var category = await SeedCategoryAsync(subject, "Card fees");
        var endsOn = Today.AddMonths(-1);
        var rule = await DefineRuleAsync(
            client, null, card, category, Today.AddMonths(-2), endsOn, "Card fee");

        var response = await MaterializeAsync(client);

        var report = Assert.Single(response.Data!.Rules, item => item.RuleId == rule);
        Assert.True(report.IsComplete);
        Assert.Equal(2, report.CreatedCount);
        await using var context = CreateContext();
        var occurrences = await context.FinancialTransactions
            .Where(item => item.RecurringTransaction!.PublicId == rule)
            .ToArrayAsync();
        Assert.All(occurrences, occurrence => Assert.NotNull(occurrence.StatementId));
    }

    [FunctionalFact]
    public async Task GivenDeletedTemplateReference_WhenMaterialized_ThenRuleIsSkippedAndOthersContinue()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Skip account");
        var deletedAccount = await CreateAccountAsync(client, "Deleted account");
        var deletedCategory = await SeedCategoryAsync(subject, "Deleted category");
        var liveCategory = await SeedCategoryAsync(subject, "Live category");
        var skippedRule = await DefineRuleAsync(
            client, account, null, deletedCategory, Today, null, "Skipped");
        var accountSkippedRule = await DefineRuleAsync(
            client, deletedAccount, null, liveCategory, Today, null, "Account skipped");
        var liveRule = await DefineRuleAsync(
            client, account, null, liveCategory, Today, null, "Created");
        await SoftDeleteCategoryAsync(deletedCategory);
        await SoftDeleteAccountAsync(deletedAccount);

        var response = await MaterializeAsync(client);

        var skipped = Assert.Single(response.Data!.Rules, item => item.RuleId == skippedRule);
        var accountSkipped = Assert.Single(
            response.Data.Rules,
            item => item.RuleId == accountSkippedRule);
        var processed = Assert.Single(response.Data.Rules, item => item.RuleId == liveRule);
        Assert.Equal("CategoryDeleted", skipped.SkipReason);
        Assert.Equal("FinancialAccountDeleted", accountSkipped.SkipReason);
        Assert.Equal(0, skipped.CreatedCount);
        Assert.Equal(0, accountSkipped.CreatedCount);
        Assert.Equal(1, processed.CreatedCount);
    }

    [FunctionalFact]
    public async Task GivenOneOccurrenceFails_WhenMaterialized_ThenLaterOccurrencesContinueAndRetryClosesGap()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Failure account");
        var category = await SeedCategoryAsync(subject, "Failure category");
        var failedOn = Today.AddDays(-7);
        var rule = await DefineRuleAsync(
            client, account, null, category, failedOn, null, "Failure isolation",
            RecurrenceFrequency.Weekly);
        await CreateFailureTriggerAsync(failedOn);
        MaterializationEnvelope first;
        try
        {
            first = await MaterializeAsync(client);
        }
        finally
        {
            await DropFailureTriggerAsync();
        }

        var firstReport = Assert.Single(first.Data!.Rules, item => item.RuleId == rule);
        Assert.Equal(1, firstReport.CreatedCount);
        Assert.Equal(RecurringTransactionMessages.OccurrenceFailed,
            Assert.Single(firstReport.Occurrences, item => item.Error is not null).Error);
        Assert.False(firstReport.IsComplete);

        var retry = await MaterializeAsync(client);

        var retryReport = Assert.Single(retry.Data!.Rules, item => item.RuleId == rule);
        Assert.Equal(1, retryReport.CreatedCount);
        await using var context = CreateContext();
        var savedRule = await context.RecurringTransactions.SingleAsync(item => item.PublicId == rule);
        Assert.Equal(Today, savedRule.LastMaterializedOn);
        Assert.Equal(2, await context.FinancialTransactions.CountAsync(item =>
            item.RecurringTransactionId == savedRule.Id));
    }

    [FunctionalFact]
    public async Task GivenMatchingImportedTransaction_WhenMaterialized_ThenOccurrenceIsFlaggedPossibleDuplicate()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Imported account");
        var category = await SeedCategoryAsync(subject, "Imported category");
        await CreateImportedTransactionAsync(client, account, category);
        var rule = await DefineRuleAsync(
            client, account, null, category, Today, null, "Imported match");

        var response = await MaterializeAsync(client);

        var report = Assert.Single(response.Data!.Rules, item => item.RuleId == rule);
        var occurrence = Assert.Single(report.Occurrences);
        Assert.True(occurrence.IsPossibleDuplicate);
        Assert.Equal(1, response.Data.PossibleDuplicateCount);
        var transaction = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{occurrence.TransactionId}");
        Assert.True(transaction?.Data?.IsPossibleDuplicate);
        Assert.Equal(rule, transaction?.Data?.RecurringTransactionId);
    }

    [FunctionalFact]
    public async Task GivenForeignOrAnonymousCaller_WhenMaterialized_ThenRulesRemainPrivate()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Private materialization");
        var category = await SeedCategoryAsync(subject, "Private recurrence");
        await DefineRuleAsync(owner, account, null, category, Today, null, "Private");
        await CreateAccountAsync(other, "Other profile");

        var foreign = await MaterializeAsync(other);
        var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/recurring-transactions/materialize", new { });

        Assert.Equal(0, foreign.Data?.CreatedCount);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await using var context = CreateContext();
        Assert.Empty(await context.FinancialTransactions.ToArrayAsync());
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

    private async Task SoftDeleteCategoryAsync(Guid categoryId)
    {
        await using var context = CreateContext();
        var category = await context.Categories.SingleAsync(item => item.PublicId == categoryId);
        category.SoftDelete(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteAccountAsync(Guid accountId)
    {
        await using var context = CreateContext();
        var account = await context.FinancialAccounts.SingleAsync(item => item.PublicId == accountId);
        account.SoftDelete(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> DefineRuleAsync(
        HttpClient client,
        Guid? accountId,
        Guid? cardId,
        Guid categoryId,
        DateOnly startsOn,
        DateOnly? endsOn,
        string description,
        RecurrenceFrequency frequency = RecurrenceFrequency.Monthly)
    {
        var response = await client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            FinancialAccountId = accountId,
            CreditCardId = cardId,
            CategoryId = categoryId,
            Direction = TransactionDirection.Expense,
            Amount = 50m,
            Frequency = frequency,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Description = description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<MaterializationEnvelope> MaterializeAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/recurring-transactions/materialize", new { });
        var envelope = (await response.Content.ReadFromJsonAsync<MaterializationEnvelope>())!;
        return envelope with { StatusCode = response.StatusCode };
    }

    private async Task CreateImportedTransactionAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Direction = TransactionDirection.Expense,
            Amount = 50m,
            OccurredOn = Today,
            Description = "Imported match"
        });
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
        await using var context = CreateContext();
        var transaction = await context.FinancialTransactions.SingleAsync(item => item.PublicId == id);
        context.Entry(transaction).Property(item => item.SourceType).CurrentValue = TransactionSourceType.Pluggy;
        await context.SaveChangesAsync();
    }

    private async Task CreateFailureTriggerAsync(DateOnly failedOn)
    {
        await using var context = CreateContext();
        var sql = $$"""
            CREATE OR REPLACE FUNCTION fortuna.fail_recurring_occurrence()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF NEW.recurring_transaction_id IS NOT NULL
                   AND NEW.occurred_on = DATE '{{failedOn:yyyy-MM-dd}}' THEN
                    RAISE EXCEPTION 'forced recurring occurrence failure';
                END IF;
                RETURN NEW;
            END;
            $function$;
            CREATE TRIGGER fail_recurring_occurrence_trigger
            BEFORE INSERT ON fortuna.financial_transaction
            FOR EACH ROW EXECUTE FUNCTION fortuna.fail_recurring_occurrence();
            """;
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task DropFailureTriggerAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS fail_recurring_occurrence_trigger
                ON fortuna.financial_transaction;
            DROP FUNCTION IF EXISTS fortuna.fail_recurring_occurrence();
            """);
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
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Materialization Owner"
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

    private sealed record MaterializationEnvelope(MaterializationData? Data)
    {
        public HttpStatusCode StatusCode { get; init; }
    }

    private sealed record MaterializationData(
        int CreatedCount,
        int PossibleDuplicateCount,
        IReadOnlyCollection<RuleReport> Rules);

    private sealed record RuleReport(
        Guid RuleId,
        int CreatedCount,
        bool IsComplete,
        string? SkipReason,
        IReadOnlyCollection<OccurrenceReport> Occurrences);

    private sealed record OccurrenceReport(
        DateOnly OccurredOn,
        Guid? TransactionId,
        bool IsPossibleDuplicate,
        string? Error);

    private sealed record TransactionEnvelope(TransactionData? Data);
    private sealed record TransactionData(Guid? RecurringTransactionId, bool IsPossibleDuplicate);
    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
}
