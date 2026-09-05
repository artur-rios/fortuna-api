using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
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

public sealed class CreditCardLifecycleTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOutstandingCard_WhenDeleted_ThenAmountAndCascadeAreRecorded()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Outstanding");
        var transactions = await AddTransactionsAsync(card.Id, preDeleteFirst: true);

        var response = await client.DeleteAsync($"/api/credit-cards/{card.Id}");
        var result = await response.Content.ReadFromJsonAsync<LifecycleEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(card.Id, result?.Data?.Id);
        Assert.Equal("BRL", result?.Data?.CurrencyCode);
        Assert.Equal(100m, result?.Data?.OutstandingAmount);
        Assert.Contains(CreditCardMessages.DeletedSuccessfully, result!.Messages);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/credit-cards/{card.Id}")).StatusCode);

        await using var context = CreateContext();
        var storedCard = await context.CreditCards.SingleAsync(item => item.PublicId == card.Id);
        var storedTransactions = await context.FinancialTransactions
            .Where(item => transactions.Ids.Contains(item.PublicId))
            .ToArrayAsync();
        Assert.True(storedCard.IsDeleted);
        Assert.All(storedTransactions, item => Assert.True(item.IsDeleted));
        Assert.Equal(storedCard.DeletionCascadeId,
            storedTransactions.Single(item => item.PublicId == transactions.ExpenseId).DeletionCascadeId);
        Assert.NotEqual(storedCard.DeletionCascadeId,
            storedTransactions.Single(item => item.PublicId == transactions.PreDeletedId).DeletionCascadeId);
    }

    [FunctionalFact]
    public async Task GivenCascadeDeletedCard_WhenRestored_ThenOnlyItsCascadeReturnsToLive()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Restorable");
        var transactions = await AddTransactionsAsync(card.Id, preDeleteFirst: true);
        (await client.DeleteAsync($"/api/credit-cards/{card.Id}")).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/credit-cards/{card.Id}/restore", null);
        var result = await response.Content.ReadFromJsonAsync<LifecycleEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100m, result?.Data?.OutstandingAmount);
        await using var context = CreateContext();
        var storedCard = await context.CreditCards.SingleAsync(item => item.PublicId == card.Id);
        var storedTransactions = await context.FinancialTransactions
            .Where(item => transactions.Ids.Contains(item.PublicId))
            .ToArrayAsync();
        Assert.False(storedCard.IsDeleted);
        Assert.False(storedTransactions.Single(item =>
            item.PublicId == transactions.ExpenseId).IsDeleted);
        Assert.False(storedTransactions.Single(item =>
            item.PublicId == transactions.CreditId).IsDeleted);
        Assert.True(storedTransactions.Single(item =>
            item.PublicId == transactions.PreDeletedId).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenLiveCard_WhenHardDeleted_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Live");

        var response = await client.DeleteAsync($"/api/credit-cards/{card.Id}/hard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(CreditCardMessages.HardDeleteRequiresSoftDeletion, body,
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedCardWithLiveCharge_WhenHardDeleted_ThenReferenceIsNamed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Referenced");
        var transactions = await AddTransactionsAsync(card.Id);
        (await client.DeleteAsync($"/api/credit-cards/{card.Id}")).EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            var transaction = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == transactions.ExpenseId);
            transaction.Restore(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/credit-cards/{card.Id}/hard");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("live transactions", body, StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenDeletedUnreferencedCard_WhenHardDeleted_ThenRowsDisappearAndAuditSurvives()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Permanent");
        var transactions = await AddTransactionsAsync(card.Id, preDeleteFirst: true);
        (await client.DeleteAsync($"/api/credit-cards/{card.Id}")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/credit-cards/{card.Id}/hard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.CreditCards.AnyAsync(item => item.PublicId == card.Id));
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            transactions.Ids.Contains(item.PublicId)));
        var audit = await context.AuditEntries
            .Where(item => item.EntityPublicId == card.Id)
            .ToArrayAsync();
        Assert.Contains(audit, item => item.Operation == nameof(DeleteCreditCardCommand));
        Assert.Contains(audit, item => item.Operation == nameof(HardDeleteCreditCardCommand));
        Assert.All(audit, item => Assert.Equal("CreditCard", item.EntityType));
    }

    [FunctionalFact]
    public async Task GivenDuplicateLiveName_WhenCardRestored_ThenConflictLeavesItDeleted()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var original = await CreateCardAsync(client, "Reused");
        (await client.DeleteAsync($"/api/credit-cards/{original.Id}")).EnsureSuccessStatusCode();
        await CreateCardAsync(client, "Reused");

        var response = await client.PostAsync($"/api/credit-cards/{original.Id}/restore", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(CreditCardMessages.DuplicateName, body, StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.True((await context.CreditCards.SingleAsync(item =>
            item.PublicId == original.Id)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrUnauthorizedCard_WhenLifecycleRequested_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(owner, "Private");
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreign = await other.DeleteAsync($"/api/credit-cards/{card.Id}");
        var missing = await other.DeleteAsync($"/api/credit-cards/{Guid.NewGuid()}");
        var anonymousResponse = await anonymous.DeleteAsync($"/api/credit-cards/{card.Id}");
        var administratorResponse = await administrator.DeleteAsync($"/api/credit-cards/{card.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
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

    private async Task<TransactionData> AddTransactionsAsync(
        Guid cardId,
        bool preDeleteFirst = false)
    {
        await using var context = CreateContext();
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == cardId);
        var category = new Category(card.User, "General", DateTimeOffset.UtcNow);
        var expense = new FinancialTransaction(
            card.User,
            card,
            category,
            TransactionDirection.Expense,
            120m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        var credit = new FinancialTransaction(
            card.User,
            card,
            category,
            TransactionDirection.Earning,
            20m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        var preDeleted = new FinancialTransaction(
            card.User,
            card,
            category,
            TransactionDirection.Expense,
            999m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow);
        if (preDeleteFirst)
        {
            preDeleted.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.FinancialTransactions.AddRange(expense, credit, preDeleted);
        await context.SaveChangesAsync();
        return new TransactionData(expense.PublicId, credit.PublicId, preDeleted.PublicId);
    }

    private static async Task<CardData> CreateCardAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 5,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardEnvelope>())!.Data!;
    }

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

    private sealed record CardEnvelope(CardData? Data);
    private sealed record CardData(Guid Id);
    private sealed record LifecycleEnvelope(LifecycleData? Data, IReadOnlyList<string> Messages);
    private sealed record LifecycleData(Guid Id, string CurrencyCode, decimal OutstandingAmount);
    private sealed record TransactionData(Guid ExpenseId, Guid CreditId, Guid PreDeletedId)
    {
        public Guid[] Ids => [ExpenseId, CreditId, PreDeletedId];
    }
}
