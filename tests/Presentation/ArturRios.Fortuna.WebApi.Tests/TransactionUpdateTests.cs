using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
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

public sealed class TransactionUpdateTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedLiveTransaction_WhenUpdated_ThenEditableFieldsAndBalanceChange()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(subject, openingBalance: 100m);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(
                seed.NewCategoryId,
                amount: 25m,
                direction: TransactionDirection.Earning,
                occurredOn: seed.OccurredOn.AddDays(1),
                description: "  Corrected entry  ",
                counterparty: "Corner Cafe",
                tags: ["Food", " food "]));
        var envelope = await response.Content.ReadFromJsonAsync<UpdateEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seed.TransactionId, envelope?.Data?.Id);
        Assert.Equal(seed.AccountId, envelope?.Data?.FinancialAccountId);
        Assert.Equal(seed.NewCategoryId, envelope?.Data?.CategoryId);
        Assert.Equal(TransactionDirection.Earning, envelope?.Data?.Direction);
        Assert.Equal(25m, envelope?.Data?.Amount);
        Assert.Equal("Corrected entry", envelope?.Data?.Description);
        Assert.Equal("Corner Cafe", envelope?.Data?.CounterpartyName);
        Assert.Single(envelope!.Data!.Tags);
        Assert.False(envelope.Data.IsManuallyCorrected);
        Assert.Contains(TransactionMessages.UpdatedSuccessfully, envelope.Messages);
        var balance = await client.GetFromJsonAsync<BalanceEnvelope>(
            $"/api/accounts/{seed.AccountId}/balance?asOf={Today:yyyy-MM-dd}");
        Assert.Equal(125m, balance?.Data?.Balance);

        await using var context = CreateContext();
        var stored = await context.FinancialTransactions
            .Include(item => item.Category)
            .Include(item => item.Counterparty)
            .Include(item => item.Tags)
            .SingleAsync(item => item.PublicId == seed.TransactionId);
        Assert.Equal(seed.AccountInternalId, stored.FinancialAccountId);
        Assert.Equal(seed.NewCategoryId, stored.Category.PublicId);
        Assert.Equal("Corner Cafe", stored.Counterparty?.Name);
        Assert.Single(stored.Tags);
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "UpdateTransactionCommand" &&
            item.EntityPublicId == seed.TransactionId);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrImmutableFields_WhenUpdated_ThenBadRequestStoresNoChange()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(subject);

        var invalidAmount = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(seed.NewCategoryId, amount: 0m));
        var targetChange = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            new
            {
                OccurredOn = seed.OccurredOn,
                Amount = 10m,
                Direction = TransactionDirection.Expense,
                CategoryId = seed.NewCategoryId,
                FinancialAccountId = seed.AccountId
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, targetChange.StatusCode);
        Assert.Contains(
            TransactionMessages.TransactionTargetImmutable,
            await targetChange.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions
            .Include(item => item.Category)
            .SingleAsync(item => item.PublicId == seed.TransactionId);
        Assert.Equal(10m, stored.Amount);
        Assert.Equal(seed.OldCategoryId, stored.Category.PublicId);
    }

    [FunctionalFact]
    public async Task GivenDeletedForeignOrMissingTransaction_WhenUpdated_ThenNotFoundIsReturned()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        await EnsureProfileAsync(owner);
        await EnsureProfileAsync(other);
        var live = await SeedAccountTransactionAsync(ownerSubject);
        var deleted = await SeedAccountTransactionAsync(ownerSubject, deleted: true);

        var foreignResponse = await other.PutAsJsonAsync(
            $"/api/transactions/{live.TransactionId}",
            Body(live.NewCategoryId));
        var deletedResponse = await owner.PutAsJsonAsync(
            $"/api/transactions/{deleted.TransactionId}",
            Body(deleted.NewCategoryId));
        var missingResponse = await owner.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}",
            Body(live.NewCategoryId));

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Contains(
            TransactionMessages.NotFound,
            await foreignResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenCardDateMovesCycle_WhenUpdated_ThenStatementsAreRebalancedAtomically()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var oldDate = Today.AddMonths(-2);
        var seed = await SeedCardTransactionAsync(subject, oldDate, 100m);
        var newDate = oldDate.AddMonths(1);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(seed.CategoryId, amount: 150m, occurredOn: newDate));
        var envelope = await response.Content.ReadFromJsonAsync<UpdateEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(seed.StatementId, envelope?.Data?.StatementId);
        await using var context = CreateContext();
        var statements = await context.CreditCardStatements
            .Where(item => item.CreditCard.PublicId == seed.CardId)
            .OrderBy(item => item.PeriodStart)
            .ToArrayAsync();
        Assert.Equal(2, statements.Length);
        Assert.Equal(0m, statements.Single(item => item.PublicId == seed.StatementId).PurchaseTotal);
        Assert.Equal(150m, statements.Single(item =>
            item.PublicId == envelope!.Data!.StatementId).PurchaseTotal);
    }

    [FunctionalFact]
    public async Task GivenDateMovesIntoSettledCycle_WhenUpdated_ThenNextOpenCycleIsUsedAsLate()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedLateReassignmentAsync(subject);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(seed.CategoryId, amount: 50m, occurredOn: seed.HistoricalDate));
        var envelope = await response.Content.ReadFromJsonAsync<UpdateEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope?.Data?.IsLateArriving);
        Assert.NotEqual(seed.OriginalStatementId, envelope?.Data?.StatementId);
        Assert.NotEqual(seed.SettledStatementId, envelope?.Data?.StatementId);
        await using var context = CreateContext();
        var original = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == seed.OriginalStatementId);
        var destination = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == envelope!.Data!.StatementId);
        Assert.Equal(0m, original.PurchaseTotal);
        Assert.Equal(50m, destination.PurchaseTotal);
        Assert.Equal(CreditCardStatementStatus.Open, destination.Status);
    }

    [FunctionalFact]
    public async Task GivenSettledStatement_WhenTotalWouldChange_ThenConflictLeavesItFrozen()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedCardTransactionAsync(
            subject,
            Today.AddMonths(-2),
            100m,
            settled: true);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(seed.CategoryId, amount: 101m, occurredOn: seed.OccurredOn));
        var dateResponse = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(
                seed.CategoryId,
                amount: 100m,
                occurredOn: seed.OccurredOn.AddMonths(1)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dateResponse.StatusCode);
        Assert.Contains(
            TransactionMessages.SettledStatementFrozen,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.TransactionId);
        var statement = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == seed.StatementId);
        Assert.Equal(100m, stored.Amount);
        Assert.Equal(100m, statement.PurchaseTotal);
    }

    [FunctionalFact]
    public async Task GivenSettledStatement_WhenOnlyMetadataChanges_ThenUpdateSucceeds()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedCardTransactionAsync(
            subject,
            Today.AddMonths(-2),
            100m,
            settled: true);
        var categoryId = await SeedCategoryAsync(subject, "Corrected category");

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(
                categoryId,
                amount: 100m,
                occurredOn: seed.OccurredOn,
                description: "Metadata only",
                tags: ["Reviewed"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions
            .Include(item => item.Category)
            .Include(item => item.Tags)
            .SingleAsync(item => item.PublicId == seed.TransactionId);
        var statement = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == seed.StatementId);
        Assert.Equal(categoryId, stored.Category.PublicId);
        Assert.Equal("Metadata only", stored.Description);
        Assert.Single(stored.Tags);
        Assert.Equal(100m, statement.PurchaseTotal);
    }

    [FunctionalFact]
    public async Task GivenTransferLeg_WhenUpdated_ThenOnlyMetadataFieldsAreAccepted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedTransferAsync(subject);

        var amountResponse = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.OutboundId}",
            Body(seed.NewCategoryId, amount: 11m, occurredOn: seed.OccurredOn));
        var metadataResponse = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.OutboundId}",
            Body(
                seed.NewCategoryId,
                amount: 10m,
                occurredOn: seed.OccurredOn,
                description: "Corrected transfer",
                tags: ["Internal"]));

        Assert.Equal(HttpStatusCode.BadRequest, amountResponse.StatusCode);
        Assert.Contains(
            TransactionMessages.TransferFieldsRestricted,
            await amountResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions
            .Include(item => item.Category)
            .Include(item => item.Tags)
            .SingleAsync(item => item.PublicId == seed.OutboundId);
        Assert.Equal(10m, stored.Amount);
        Assert.Equal(seed.NewCategoryId, stored.Category.PublicId);
        Assert.Equal("Corrected transfer", stored.Description);
        Assert.Single(stored.Tags);
    }

    [FunctionalFact]
    public async Task GivenImportedTransaction_WhenUpdated_ThenSourceSurvivesAndCorrectionIsMarked()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(
            subject,
            sourceType: TransactionSourceType.Excel);

        var response = await client.PutAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}",
            Body(seed.NewCategoryId, description: "Manual correction"));
        var envelope = await response.Content.ReadFromJsonAsync<UpdateEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TransactionSourceType.Excel, envelope?.Data?.SourceType);
        Assert.True(envelope?.Data?.IsManuallyCorrected);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.TransactionId);
        Assert.Equal(TransactionSourceType.Excel, stored.SourceType);
        Assert.True(stored.IsManuallyCorrected);
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenUpdated_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var body = Body(Guid.NewGuid());

        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}", body);
        var administratorResponse = await administrator.PutAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}", body);

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

    private async Task<AccountSeed> SeedAccountTransactionAsync(
        Guid subject,
        decimal openingBalance = 0m,
        bool deleted = false,
        TransactionSourceType sourceType = TransactionSourceType.Manual)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var account = new FinancialAccount(
            user,
            $"Account {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            openingBalance,
            DateTimeOffset.UtcNow);
        var oldCategory = new Category(user, $"Old {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var newCategory = new Category(user, $"New {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var oldTag = new Tag(user, $"Old tag {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var occurredOn = Today.AddDays(-2);
        var transaction = new FinancialTransaction(
            user,
            account,
            oldCategory,
            TransactionDirection.Expense,
            10m,
            occurredOn,
            DateTimeOffset.UtcNow,
            "Before",
            tags: [oldTag]);
        if (sourceType != TransactionSourceType.Manual)
        {
            context.Entry(transaction).Property(item => item.SourceType).CurrentValue = sourceType;
        }

        if (deleted)
        {
            transaction.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.AddRange(account, oldCategory, newCategory, oldTag, transaction);
        await context.SaveChangesAsync();
        return new AccountSeed(
            transaction.PublicId,
            account.PublicId,
            account.Id,
            oldCategory.PublicId,
            newCategory.PublicId,
            occurredOn);
    }

    private async Task<CardSeed> SeedCardTransactionAsync(
        Guid subject,
        DateOnly occurredOn,
        decimal amount,
        bool settled = false)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var card = new CreditCard(
            user,
            $"Card {Guid.NewGuid():N}",
            "Issuer",
            currency,
            1000m,
            15,
            5,
            null,
            DateTimeOffset.UtcNow);
        var category = new Category(user, $"Card category {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Expense,
            amount,
            occurredOn,
            DateTimeOffset.UtcNow);
        var statement = new CreditCardStatement(
            card,
            BillingCycle.Containing(occurredOn, card.ClosingDay, card.DueDay),
            DateTimeOffset.UtcNow);
        transaction.AssignToStatement(statement, false, DateTimeOffset.UtcNow);
        statement.RecalculatePurchaseTotal(amount, DateTimeOffset.UtcNow);
        context.AddRange(card, category, statement, transaction);
        if (settled)
        {
            var payment = new FinancialTransaction(
                user,
                card,
                category,
                TransactionDirection.Earning,
                amount,
                occurredOn,
                DateTimeOffset.UtcNow);
            statement.Close(DateTimeOffset.UtcNow);
            statement.Settle(payment, DateTimeOffset.UtcNow);
            context.Add(payment);
        }

        await context.SaveChangesAsync();
        return new CardSeed(
            transaction.PublicId,
            card.PublicId,
            category.PublicId,
            statement.PublicId,
            occurredOn);
    }

    private async Task<TransferSeed> SeedTransferAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var origin = new FinancialAccount(
            user,
            $"Origin {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            100m,
            DateTimeOffset.UtcNow);
        var destination = new FinancialAccount(
            user,
            $"Destination {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            0m,
            DateTimeOffset.UtcNow);
        var oldCategory = new Category(user, $"Transfer {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var newCategory = new Category(user, $"Transfer new {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var occurredOn = Today.AddDays(-2);
        var outbound = new FinancialTransaction(
            user,
            origin,
            oldCategory,
            TransactionDirection.Expense,
            10m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var inbound = new FinancialTransaction(
            user,
            destination,
            oldCategory,
            TransactionDirection.Earning,
            10m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var transfer = new Transfer(outbound, inbound, null, null, DateTimeOffset.UtcNow);
        context.AddRange(origin, destination, oldCategory, newCategory, outbound, inbound, transfer);
        await context.SaveChangesAsync();
        return new TransferSeed(outbound.PublicId, newCategory.PublicId, occurredOn);
    }

    private async Task<LateReassignmentSeed> SeedLateReassignmentAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var card = new CreditCard(
            user,
            $"Late card {Guid.NewGuid():N}",
            "Issuer",
            currency,
            1000m,
            15,
            5,
            null,
            DateTimeOffset.UtcNow);
        var category = new Category(user, $"Late category {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var currentDate = Today.AddDays(-2);
        var historicalDate = Today.AddMonths(-3);
        var transaction = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Expense,
            50m,
            currentDate,
            DateTimeOffset.UtcNow);
        var currentStatement = new CreditCardStatement(
            card,
            BillingCycle.Containing(currentDate, card.ClosingDay, card.DueDay),
            DateTimeOffset.UtcNow);
        transaction.AssignToStatement(currentStatement, false, DateTimeOffset.UtcNow);
        currentStatement.RecalculatePurchaseTotal(50m, DateTimeOffset.UtcNow);
        var historicalStatement = new CreditCardStatement(
            card,
            BillingCycle.Containing(historicalDate, card.ClosingDay, card.DueDay),
            DateTimeOffset.UtcNow);
        var payment = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Earning,
            1m,
            historicalDate,
            DateTimeOffset.UtcNow);
        historicalStatement.Close(DateTimeOffset.UtcNow);
        historicalStatement.Settle(payment, DateTimeOffset.UtcNow);
        context.AddRange(
            card,
            category,
            transaction,
            currentStatement,
            historicalStatement,
            payment);
        await context.SaveChangesAsync();
        return new LateReassignmentSeed(
            transaction.PublicId,
            category.PublicId,
            currentStatement.PublicId,
            historicalStatement.PublicId,
            historicalDate);
    }

    private async Task<Guid> SeedCategoryAsync(Guid subject, string name)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var category = new Category(user, name, DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.PublicId;
    }

    private static Task<Domain.Users.UserProfile> UserAsync(
        AppDbContext context,
        Guid subject) => context.UserProfiles.SingleAsync(item =>
        item.ExternalSubject == subject.ToString("D"));

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
        Guid categoryId,
        decimal amount = 10m,
        TransactionDirection direction = TransactionDirection.Expense,
        DateOnly? occurredOn = null,
        string? description = null,
        string? counterparty = null,
        string[]? tags = null) => new
        {
            OccurredOn = occurredOn ?? Today.AddDays(-2),
            Amount = amount,
            Direction = direction,
            CategoryId = categoryId,
            Description = description,
            Counterparty = counterparty,
            Tags = tags
        };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Transaction Owner"
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

    private sealed record AccountSeed(
        Guid TransactionId,
        Guid AccountId,
        long AccountInternalId,
        Guid OldCategoryId,
        Guid NewCategoryId,
        DateOnly OccurredOn);
    private sealed record CardSeed(
        Guid TransactionId,
        Guid CardId,
        Guid CategoryId,
        Guid StatementId,
        DateOnly OccurredOn);
    private sealed record TransferSeed(Guid OutboundId, Guid NewCategoryId, DateOnly OccurredOn);
    private sealed record LateReassignmentSeed(
        Guid TransactionId,
        Guid CategoryId,
        Guid OriginalStatementId,
        Guid SettledStatementId,
        DateOnly HistoricalDate);
    private sealed record UpdateEnvelope(
        UpdateData? Data,
        IReadOnlyCollection<string> Messages);
    private sealed record UpdateData(
        Guid Id,
        Guid? FinancialAccountId,
        Guid? CreditCardId,
        Guid CategoryId,
        TransactionDirection Direction,
        decimal Amount,
        string? Description,
        string? CounterpartyName,
        TransactionSourceType SourceType,
        bool IsManuallyCorrected,
        bool IsLateArriving,
        Guid? StatementId,
        IReadOnlyCollection<TagData> Tags);
    private sealed record TagData(Guid Id, string Name);
    private sealed record BalanceEnvelope(BalanceData? Data);
    private sealed record BalanceData(decimal Balance);
}
