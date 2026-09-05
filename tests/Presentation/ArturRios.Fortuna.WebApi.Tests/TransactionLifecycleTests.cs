using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Investments;
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

public sealed class TransactionLifecycleTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLiveTransaction_WhenDeletedAndRestored_ThenFiguresAndAuditFollowState()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(subject, openingBalance: 100m, amount: 25m);

        var delete = await client.DeleteAsync($"/api/transactions/{seed.TransactionId}");
        var deletedBalance = await BalanceAsync(client, seed.AccountId);
        var hidden = await client.GetAsync($"/api/transactions/{seed.TransactionId}");
        var deleted = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{seed.TransactionId}?includeDeleted=true");
        var restore = await client.PostAsync(
            $"/api/transactions/{seed.TransactionId}/restore",
            null);
        var restoredBalance = await BalanceAsync(client, seed.AccountId);

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Contains(
            TransactionMessages.DeletedSuccessfully,
            await delete.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(100m, deletedBalance);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(deleted?.Data?.IsDeleted);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(75m, restoredBalance);
        await using var context = CreateContext();
        var stored = await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.TransactionId);
        Assert.False(stored.IsDeleted);
        var audit = await context.AuditEntries
            .Where(item => item.EntityPublicId == seed.TransactionId)
            .ToArrayAsync();
        Assert.Contains(audit, item =>
            item.Operation == "DeleteTransactionCommand" &&
            item.Outcome == AuditOutcome.Succeeded);
        Assert.Contains(audit, item =>
            item.Operation == "RestoreTransactionCommand" &&
            item.Outcome == AuditOutcome.Succeeded);
    }

    [FunctionalFact]
    public async Task GivenLiveTransaction_WhenHardDeleted_ThenConflictUntilSoftDeletion()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(subject);

        var refused = await client.DeleteAsync(
            $"/api/transactions/{seed.TransactionId}/hard");
        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}"))
            .EnsureSuccessStatusCode();
        var deleted = await client.DeleteAsync(
            $"/api/transactions/{seed.TransactionId}/hard");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains(
            TransactionMessages.HardDeleteRequiresSoftDeletion,
            await refused.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            item.PublicId == seed.TransactionId));
        var audits = await context.AuditEntries
            .Where(item => item.Operation == "HardDeleteTransactionCommand")
            .OrderBy(item => item.OccurredAt)
            .ToArrayAsync();
        Assert.Equal([AuditOutcome.Refused, AuditOutcome.Succeeded],
            audits.Select(item => item.Outcome));
    }

    [FunctionalFact]
    public async Task GivenTransferLeg_WhenLifecycleRuns_ThenPairMovesAtomically()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedTransferAsync(subject);

        var delete = await client.DeleteAsync($"/api/transactions/{seed.OutboundId}");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        await using (var context = CreateContext())
        {
            var legs = await context.FinancialTransactions
                .Where(item => item.PublicId == seed.OutboundId ||
                    item.PublicId == seed.InboundId)
                .ToArrayAsync();
            var transfer = await context.Transfers.SingleAsync(item =>
                item.PublicId == seed.TransferId);
            Assert.All(legs, item => Assert.True(item.IsDeleted));
            Assert.True(transfer.IsDeleted);
            Assert.Single(legs.Select(item => item.DeletionCascadeId).Distinct());
            Assert.Equal(transfer.DeletionCascadeId, legs[0].DeletionCascadeId);
        }

        Assert.Equal(100m, await BalanceAsync(client, seed.OriginId));
        Assert.Equal(0m, await BalanceAsync(client, seed.DestinationId));
        var restore = await client.PostAsync(
            $"/api/transactions/{seed.InboundId}/restore",
            null);

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(90m, await BalanceAsync(client, seed.OriginId));
        Assert.Equal(10m, await BalanceAsync(client, seed.DestinationId));
        await using (var context = CreateContext())
        {
            Assert.All(await context.FinancialTransactions
                .Where(item => item.PublicId == seed.OutboundId ||
                    item.PublicId == seed.InboundId)
                .ToArrayAsync(), item => Assert.False(item.IsDeleted));
            Assert.False((await context.Transfers.SingleAsync(item =>
                item.PublicId == seed.TransferId)).IsDeleted);
        }

        (await client.DeleteAsync($"/api/transactions/{seed.InboundId}"))
            .EnsureSuccessStatusCode();
        var hardDelete = await client.DeleteAsync(
            $"/api/transactions/{seed.OutboundId}/hard");

        Assert.Equal(HttpStatusCode.OK, hardDelete.StatusCode);
        await using (var context = CreateContext())
        {
            Assert.False(await context.FinancialTransactions.AnyAsync(item =>
                item.PublicId == seed.OutboundId || item.PublicId == seed.InboundId));
            Assert.False(await context.Transfers.AnyAsync(item =>
                item.PublicId == seed.TransferId));
        }
    }

    [FunctionalFact]
    public async Task GivenOpenCardStatement_WhenTransactionLifecycleRuns_ThenTotalIsRebalanced()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedOpenCardTransactionAsync(subject);

        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}"))
            .EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            Assert.Equal(0m, (await context.CreditCardStatements.SingleAsync(item =>
                item.PublicId == seed.StatementId)).PurchaseTotal);
        }

        (await client.PostAsync(
            $"/api/transactions/{seed.TransactionId}/restore",
            null)).EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            Assert.Equal(50m, (await context.CreditCardStatements.SingleAsync(item =>
                item.PublicId == seed.StatementId)).PurchaseTotal);
        }
    }

    [FunctionalFact]
    public async Task GivenInconsistentTransfer_WhenLegDeleted_ThenBothSidesAreNormalized()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedTransferAsync(subject);
        await using (var context = CreateContext())
        {
            var inbound = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == seed.InboundId);
            inbound.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/transactions/{seed.OutboundId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verification = CreateContext();
        var legs = await verification.FinancialTransactions
            .Where(item => item.PublicId == seed.OutboundId || item.PublicId == seed.InboundId)
            .ToArrayAsync();
        var transfer = await verification.Transfers.SingleAsync(item =>
            item.PublicId == seed.TransferId);
        Assert.All(legs, item => Assert.True(item.IsDeleted));
        Assert.All(legs, item => Assert.Equal(transfer.DeletionCascadeId,
            item.DeletionCascadeId));
    }

    [FunctionalFact]
    public async Task GivenInvestmentTransferLeg_WhenDeletedAndRestored_ThenMovementFollowsIt()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedInvestmentTransferAsync(subject);

        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}"))
            .EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            Assert.True((await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == seed.TransactionId)).IsDeleted);
            Assert.True((await context.InvestmentMovements.SingleAsync(item =>
                item.PublicId == seed.MovementId)).IsDeleted);
            Assert.True((await context.Transfers.SingleAsync(item =>
                item.PublicId == seed.TransferId)).IsDeleted);
        }

        (await client.PostAsync(
            $"/api/transactions/{seed.TransactionId}/restore",
            null)).EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            Assert.False((await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == seed.TransactionId)).IsDeleted);
            Assert.False((await context.InvestmentMovements.SingleAsync(item =>
                item.PublicId == seed.MovementId)).IsDeleted);
            Assert.False((await context.Transfers.SingleAsync(item =>
                item.PublicId == seed.TransferId)).IsDeleted);
        }


        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}"))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}/hard"))
            .EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            Assert.False(await context.FinancialTransactions.AnyAsync(item =>
                item.PublicId == seed.TransactionId));
            Assert.False(await context.InvestmentMovements.AnyAsync(item =>
                item.PublicId == seed.MovementId));
            Assert.False(await context.Transfers.AnyAsync(item =>
                item.PublicId == seed.TransferId));
        }
    }

    [FunctionalFact]
    public async Task GivenSettledStatement_WhenTransactionOrSettlementDeleted_ThenConflictIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedSettledStatementAsync(subject);

        var charge = await client.DeleteAsync($"/api/transactions/{seed.ChargeId}");
        var settlement = await client.DeleteAsync(
            $"/api/transactions/{seed.SettlementOutboundId}");

        Assert.Equal(HttpStatusCode.Conflict, charge.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, settlement.StatusCode);
        Assert.Contains(
            TransactionMessages.SettledStatementFrozen,
            await settlement.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False((await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.ChargeId)).IsDeleted);
        Assert.False((await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.SettlementOutboundId)).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenImportedTransaction_WhenDeletedAndRestored_ThenImportProvenanceSurvives()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAccountTransactionAsync(
            subject,
            sourceType: TransactionSourceType.Excel);

        (await client.DeleteAsync($"/api/transactions/{seed.TransactionId}"))
            .EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            var transaction = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == seed.TransactionId);
            Assert.True(transaction.IsDeleted);
            Assert.Equal(TransactionSourceType.Excel, transaction.SourceType);
        }

        (await client.PostAsync(
            $"/api/transactions/{seed.TransactionId}/restore",
            null)).EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            var transaction = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == seed.TransactionId);
            Assert.False(transaction.IsDeleted);
            Assert.Equal(TransactionSourceType.Excel, transaction.SourceType);
        }
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrUnauthorizedTransaction_WhenDeleted_ThenAccessIsRefused()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        await EnsureProfileAsync(owner);
        await EnsureProfileAsync(other);
        var seed = await SeedAccountTransactionAsync(ownerSubject);

        var foreign = await other.DeleteAsync($"/api/transactions/{seed.TransactionId}");
        var missing = await owner.DeleteAsync($"/api/transactions/{Guid.NewGuid()}");
        var unauthenticated = await anonymous.DeleteAsync(
            $"/api/transactions/{seed.TransactionId}");
        var forbidden = await administrator.DeleteAsync(
            $"/api/transactions/{seed.TransactionId}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
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
        decimal amount = 10m,
        TransactionSourceType sourceType = TransactionSourceType.Manual)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await CurrencyAsync(context);
        var account = Account(user, currency, openingBalance);
        var category = Category(user);
        var transaction = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            amount,
            Today.AddDays(-2),
            DateTimeOffset.UtcNow);
        if (sourceType != TransactionSourceType.Manual)
        {
            context.Entry(transaction).Property(item => item.SourceType).CurrentValue = sourceType;
        }

        context.AddRange(account, category, transaction);
        await context.SaveChangesAsync();
        return new AccountSeed(transaction.PublicId, account.PublicId);
    }

    private async Task<TransferSeed> SeedTransferAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await CurrencyAsync(context);
        var origin = Account(user, currency, 100m);
        var destination = Account(user, currency, 0m);
        var category = Category(user);
        var occurredOn = Today.AddDays(-2);
        var outbound = new FinancialTransaction(
            user,
            origin,
            category,
            TransactionDirection.Expense,
            10m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var inbound = new FinancialTransaction(
            user,
            destination,
            category,
            TransactionDirection.Earning,
            10m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var transfer = new Transfer(outbound, inbound, null, null, DateTimeOffset.UtcNow);
        context.AddRange(origin, destination, category, outbound, inbound, transfer);
        await context.SaveChangesAsync();
        return new TransferSeed(
            transfer.PublicId,
            outbound.PublicId,
            inbound.PublicId,
            origin.PublicId,
            destination.PublicId);
    }

    private async Task<InvestmentTransferSeed> SeedInvestmentTransferAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await CurrencyAsync(context);
        var account = Account(user, currency, 100m);
        var category = Category(user);
        var investment = new Investment(
            user,
            $"Investment {Guid.NewGuid():N}",
            null,
            InvestmentType.Fund,
            currency,
            DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            10m,
            Today.AddDays(-2),
            DateTimeOffset.UtcNow);
        var movement = new InvestmentMovement(
            investment,
            InvestmentMovementType.Contribution,
            10m,
            Today.AddDays(-2),
            DateTimeOffset.UtcNow);
        var transfer = new Transfer(transaction, movement, null, null, DateTimeOffset.UtcNow);
        context.AddRange(account, category, investment, transaction, movement, transfer);
        await context.SaveChangesAsync();
        return new InvestmentTransferSeed(
            transfer.PublicId,
            transaction.PublicId,
            movement.PublicId);
    }

    private async Task<SettledSeed> SeedSettledStatementAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await CurrencyAsync(context);
        var account = Account(user, currency, 100m);
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
        var category = Category(user);
        var occurredOn = Today.AddMonths(-2);
        var charge = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Expense,
            50m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var statement = new CreditCardStatement(
            card,
            BillingCycle.Containing(occurredOn, card.ClosingDay, card.DueDay),
            DateTimeOffset.UtcNow);
        charge.AssignToStatement(statement, false, DateTimeOffset.UtcNow);
        statement.RecalculatePurchaseTotal(50m, DateTimeOffset.UtcNow);
        var outbound = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            50m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var inbound = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Earning,
            50m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var transfer = new Transfer(outbound, inbound, null, null, DateTimeOffset.UtcNow);
        statement.Close(DateTimeOffset.UtcNow);
        statement.Settle(inbound, DateTimeOffset.UtcNow);
        context.AddRange(
            account,
            card,
            category,
            charge,
            statement,
            outbound,
            inbound,
            transfer);
        await context.SaveChangesAsync();
        return new SettledSeed(charge.PublicId, outbound.PublicId);
    }

    private async Task<CardSeed> SeedOpenCardTransactionAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await CurrencyAsync(context);
        var card = new CreditCard(
            user,
            $"Open card {Guid.NewGuid():N}",
            "Issuer",
            currency,
            1000m,
            15,
            5,
            null,
            DateTimeOffset.UtcNow);
        var category = Category(user);
        var occurredOn = Today.AddDays(-2);
        var transaction = new FinancialTransaction(
            user,
            card,
            category,
            TransactionDirection.Expense,
            50m,
            occurredOn,
            DateTimeOffset.UtcNow);
        var statement = new CreditCardStatement(
            card,
            BillingCycle.Containing(occurredOn, card.ClosingDay, card.DueDay),
            DateTimeOffset.UtcNow);
        transaction.AssignToStatement(statement, false, DateTimeOffset.UtcNow);
        statement.RecalculatePurchaseTotal(50m, DateTimeOffset.UtcNow);
        context.AddRange(card, category, transaction, statement);
        await context.SaveChangesAsync();
        return new CardSeed(transaction.PublicId, statement.PublicId);
    }

    private static FinancialAccount Account(
        Domain.Users.UserProfile user,
        Domain.Currencies.Currency currency,
        decimal openingBalance) => new(
        user,
        $"Account {Guid.NewGuid():N}",
        null,
        FinancialAccountType.Checking,
        currency,
        openingBalance,
        DateTimeOffset.UtcNow);

    private static Category Category(Domain.Users.UserProfile user) => new(
        user,
        $"Category {Guid.NewGuid():N}",
        DateTimeOffset.UtcNow);

    private static Task<Domain.Users.UserProfile> UserAsync(
        AppDbContext context,
        Guid subject) => context.UserProfiles.SingleAsync(item =>
        item.ExternalSubject == subject.ToString("D"));

    private static Task<Domain.Currencies.Currency> CurrencyAsync(AppDbContext context) =>
        context.Currencies.SingleAsync(item => item.Code == "BRL");

    private static async Task<decimal?> BalanceAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<BalanceEnvelope>(
            $"/api/accounts/{accountId}/balance?asOf={Today:yyyy-MM-dd}"))?.Data?.Balance;

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

    private sealed record AccountSeed(Guid TransactionId, Guid AccountId);
    private sealed record TransferSeed(
        Guid TransferId,
        Guid OutboundId,
        Guid InboundId,
        Guid OriginId,
        Guid DestinationId);
    private sealed record InvestmentTransferSeed(
        Guid TransferId,
        Guid TransactionId,
        Guid MovementId);
    private sealed record SettledSeed(Guid ChargeId, Guid SettlementOutboundId);
    private sealed record CardSeed(Guid TransactionId, Guid StatementId);
    private sealed record BalanceEnvelope(BalanceData? Data);
    private sealed record BalanceData(decimal Balance);
    private sealed record TransactionEnvelope(TransactionData? Data);
    private sealed record TransactionData(bool IsDeleted);
}
