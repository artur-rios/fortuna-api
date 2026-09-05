using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
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

public sealed class TransferRecordingTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedAccounts_WhenTransferred_ThenPairedLegsAndBalancesMoveAtomically()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Origin", "BRL", 100m);
        var destination = await CreateAccountAsync(client, "Destination", "BRL", 10m);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            Request(origin, destination, 25m));
        var envelope = await response.Content.ReadFromJsonAsync<TransferEnvelope>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(TransferMessages.RecordedSuccessfully, envelope!.Messages);
        var data = envelope.Data!;
        Assert.Equal(origin, data.OriginFinancialAccountId);
        Assert.Equal(destination, data.DestinationFinancialAccountId);
        Assert.Equal(25m, data.OutboundAmount);
        Assert.Equal(25m, data.InboundAmount);
        Assert.Equal("BRL", data.OutboundCurrencyCode);
        Assert.Equal("BRL", data.InboundCurrencyCode);
        Assert.Null(data.AppliedRate);
        Assert.Equal(75m, await BalanceAsync(client, origin));
        Assert.Equal(35m, await BalanceAsync(client, destination));

        var search = await client.GetFromJsonAsync<SearchEnvelope>("/api/transactions");
        Assert.Equal(2, search?.Data?.Items.Count);
        Assert.All(search!.Data!.Items, transaction => Assert.True(transaction.IsTransfer));
        Assert.Empty(search.Data.Totals.ByCurrency);

        await using var context = CreateContext();
        var transfer = await context.Transfers
            .Include(item => item.OutboundTransaction)
            .Include(item => item.InboundTransaction)
            .SingleAsync(item => item.PublicId == data.Id);
        Assert.Equal(data.OutboundTransactionId,
            transfer.OutboundTransaction.PublicId);
        Assert.Equal(data.InboundTransactionId,
            transfer.InboundTransaction!.PublicId);
        Assert.Equal(TransactionDirection.Expense, transfer.OutboundTransaction.Direction);
        Assert.Equal(TransactionDirection.Earning, transfer.InboundTransaction.Direction);
        Assert.Contains(await context.AuditEntries.ToArrayAsync(), item =>
            item.Operation == nameof(RecordTransferCommand) &&
            item.EntityPublicId == transfer.PublicId &&
            item.Outcome == AuditOutcome.Succeeded);
    }

    [FunctionalFact]
    public async Task GivenDifferentCurrencies_WhenTransferred_ThenLatestRateAndOriginalValuePersist()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Dollar", "USD", 100m);
        var destination = await CreateAccountAsync(client, "Real", "BRL", 0m);
        var olderDate = Today.AddDays(-2);
        var latestDate = Today.AddDays(-1);
        await SeedRateAsync("USD", "BRL", 4m, olderDate);
        await SeedRateAsync("USD", "BRL", 5.123m, latestDate);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            Request(origin, destination, 2.005m));
        var transfer = (await response.Content.ReadFromJsonAsync<TransferEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2.005m, transfer.OutboundAmount);
        Assert.Equal("USD", transfer.OutboundCurrencyCode);
        Assert.Equal(10.27m, transfer.InboundAmount);
        Assert.Equal("BRL", transfer.InboundCurrencyCode);
        Assert.Equal(5.123m, transfer.AppliedRate);
        Assert.Equal(latestDate, transfer.RateDate);
        await using var context = CreateContext();
        var stored = await context.Transfers.SingleAsync(item => item.PublicId == transfer.Id);
        var inbound = await context.FinancialTransactions
            .Include(item => item.OriginalCurrency)
            .SingleAsync(item => item.PublicId == transfer.InboundTransactionId);
        Assert.Equal(5.123m, stored.AppliedRate);
        Assert.Equal(latestDate, stored.RateDate);
        Assert.Equal(2.005m, inbound.OriginalAmount);
        Assert.Equal("USD", inbound.OriginalCurrency?.Code);
        Assert.Equal(5.123m, inbound.AppliedRate);
    }

    [FunctionalFact]
    public async Task GivenNoExchangeRate_WhenTransferred_ThenNoLegOrTransferIsPersisted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "No rate origin", "USD", 100m);
        var destination = await CreateAccountAsync(client, "No rate destination", "EUR", 0m);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            Request(origin, destination, 10m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(TransferMessages.ExchangeRateUnavailable,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Empty(await context.Transfers.ToArrayAsync());
        Assert.Empty(await context.FinancialTransactions.ToArrayAsync());
        Assert.Equal(100m, await BalanceAsync(client, origin));
        Assert.Equal(0m, await BalanceAsync(client, destination));
    }

    [FunctionalFact]
    public async Task GivenSameAccount_WhenTransferred_ThenBadRequestCreatesNothing()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Same", "BRL", 100m);

        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            Request(account, account, 10m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(TransferMessages.AccountsMustDiffer,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Empty(await context.Transfers.ToArrayAsync());
        Assert.Empty(await context.FinancialTransactions.ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignDeletedOrMissingAccount_WhenTransferred_ThenNotFoundCreatesNothing()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        var owned = await CreateAccountAsync(owner, "Owned", "BRL", 100m);
        var deleted = await CreateAccountAsync(owner, "Deleted", "BRL", 0m);
        var foreign = await CreateAccountAsync(other, "Foreign", "BRL", 0m);
        (await owner.DeleteAsync($"/api/accounts/{deleted}")).EnsureSuccessStatusCode();

        var foreignResponse = await owner.PostAsJsonAsync(
            "/api/transfers",
            Request(owned, foreign, 10m));
        var deletedResponse = await owner.PostAsJsonAsync(
            "/api/transfers",
            Request(owned, deleted, 10m));
        var missingResponse = await owner.PostAsJsonAsync(
            "/api/transfers",
            Request(Guid.NewGuid(), owned, 10m));

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Contains(TransferMessages.DestinationFinancialAccountNotFound,
            await foreignResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(TransferMessages.DestinationFinancialAccountNotFound,
            await deletedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(TransferMessages.OriginFinancialAccountNotFound,
            await missingResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Empty(await context.Transfers.ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenClosedCardStatement_WhenTransferred_ThenUc23SettlesIt()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Card payer", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Destination card");
        var categoryId = await SeedCategoryAsync(subject, "Purchases");
        var charge = await RecordChargeAsync(client, card, categoryId, 100m);
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/transfers", new
        {
            OriginFinancialAccountId = origin,
            DestinationStatementId = charge.StatementId,
            Amount = 100m,
            OccurredOn = Today
        });
        var transfer = (await response.Content.ReadFromJsonAsync<TransferEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(charge.StatementId, transfer.DestinationStatementId);
        Assert.Null(transfer.DestinationFinancialAccountId);
        Assert.Equal(900m, await BalanceAsync(client, origin));
        await using var context = CreateContext();
        var statement = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == charge.StatementId);
        Assert.Equal("Settled", statement.Status.ToString());
        Assert.Equal(transfer.InboundTransactionId,
            (await context.FinancialTransactions.SingleAsync(item =>
                item.Id == statement.SettlementTransactionId)).PublicId);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrUnauthorizedTransfer_WhenPosted_ThenAccessAndFieldsAreRejected()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var invalid = await owner.PostAsJsonAsync("/api/transfers", new
        {
            OriginFinancialAccountId = Guid.Empty,
            DestinationFinancialAccountId = Guid.Empty,
            Amount = 0m,
            OccurredOn = default(DateOnly),
            OwnerId = Guid.NewGuid()
        });
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var body = Request(Guid.NewGuid(), Guid.NewGuid(), 1m);

        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/transfers", body);
        var administratorResponse = await administrator.PostAsJsonAsync("/api/transfers", body);
        var invalidBody = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(TransferMessages.OriginFinancialAccountIdRequired, invalidBody);
        Assert.Contains(TransferMessages.ExactlyOneDestinationRequired, invalidBody);
        Assert.Contains(TransferMessages.AmountPositive, invalidBody);
        Assert.Contains(TransferMessages.OccurredOnRequired, invalidBody);
        Assert.Contains(TransferMessages.OwnerImmutable, invalidBody);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenLiveTransfer_WhenDeletedAndRestored_ThenBothBalancesAndReadsFollowState()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Lifecycle origin", "BRL", 100m);
        var destination = await CreateAccountAsync(
            client,
            "Lifecycle destination",
            "BRL",
            0m);
        var transfer = await RecordTransferAsync(client, origin, destination, 10m);

        var before = await client.GetFromJsonAsync<TransferEnvelope>(
            $"/api/transfers/{transfer.Id}");
        var deleted = await client.DeleteAsync($"/api/transfers/{transfer.Id}");
        var deletedOriginBalance = await BalanceAsync(client, origin);
        var deletedDestinationBalance = await BalanceAsync(client, destination);
        var hidden = await client.GetAsync($"/api/transfers/{transfer.Id}");
        var tombstone = await client.GetFromJsonAsync<TransferEnvelope>(
            $"/api/transfers/{transfer.Id}?includeDeleted=true");
        var restored = await client.PostAsync($"/api/transfers/{transfer.Id}/restore", null);
        var restoredOriginBalance = await BalanceAsync(client, origin);
        var restoredDestinationBalance = await BalanceAsync(client, destination);
        var after = await client.GetFromJsonAsync<TransferEnvelope>(
            $"/api/transfers/{transfer.Id}");

        Assert.Equal(transfer.Id, before?.Data?.Id);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(100m, deletedOriginBalance);
        Assert.Equal(0m, deletedDestinationBalance);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.True(tombstone?.Data?.IsDeleted);
        Assert.True(tombstone?.Data?.OutboundIsDeleted);
        Assert.True(tombstone?.Data?.InboundIsDeleted);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.False(after?.Data?.IsDeleted);
        Assert.Equal(90m, restoredOriginBalance);
        Assert.Equal(10m, restoredDestinationBalance);
        await using var context = CreateContext();
        var audits = await context.AuditEntries
            .Where(item => item.EntityPublicId == transfer.Id)
            .ToArrayAsync();
        Assert.Contains(audits, item =>
            item.Operation == nameof(DeleteTransferCommand) &&
            item.Outcome == AuditOutcome.Succeeded);
        Assert.Contains(audits, item =>
            item.Operation == nameof(RestoreTransferCommand) &&
            item.Outcome == AuditOutcome.Succeeded);
    }

    [FunctionalFact]
    public async Task GivenSingleLegDeletion_WhenRequested_ThenTheWholeTransferIsDeleted()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Leg origin", "BRL", 100m);
        var destination = await CreateAccountAsync(client, "Leg destination", "BRL", 0m);
        var transfer = await RecordTransferAsync(client, origin, destination, 10m);

        var response = await client.DeleteAsync(
            $"/api/transactions/{transfer.InboundTransactionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = CreateContext();
        var stored = await context.Transfers.SingleAsync(item => item.PublicId == transfer.Id);
        var legs = await context.FinancialTransactions
            .Where(item => item.PublicId == transfer.OutboundTransactionId ||
                item.PublicId == transfer.InboundTransactionId)
            .ToArrayAsync();
        Assert.True(stored.IsDeleted);
        Assert.All(legs, item => Assert.True(item.IsDeleted));
        Assert.All(legs, item => Assert.Equal(stored.DeletionCascadeId,
            item.DeletionCascadeId));
    }

    [FunctionalFact]
    public async Task GivenInconsistentLegs_WhenTransferDeleted_ThenThePairIsNormalizedAndAudited()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Repair origin", "BRL", 100m);
        var destination = await CreateAccountAsync(client, "Repair destination", "BRL", 0m);
        var transfer = await RecordTransferAsync(client, origin, destination, 10m);
        await using (var context = CreateContext())
        {
            var inbound = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == transfer.InboundTransactionId);
            inbound.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/transfers/{transfer.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var assertionContext = CreateContext();
        var stored = await assertionContext.Transfers.SingleAsync(item =>
            item.PublicId == transfer.Id);
        var legs = await assertionContext.FinancialTransactions
            .Where(item => item.PublicId == transfer.OutboundTransactionId ||
                item.PublicId == transfer.InboundTransactionId)
            .ToArrayAsync();
        Assert.All(legs, item => Assert.True(item.IsDeleted));
        Assert.All(legs, item => Assert.Equal(stored.DeletionCascadeId,
            item.DeletionCascadeId));
        Assert.Contains(await assertionContext.AuditEntries.ToArrayAsync(), item =>
            item.Operation == nameof(DeleteTransferCommand) &&
            item.EntityPublicId == transfer.Id &&
            item.Outcome == AuditOutcome.Succeeded);
    }

    [FunctionalFact]
    public async Task GivenStatementSettlement_WhenTransferDeleted_ThenFrozenConflictPreservesIt()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Frozen payer", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Frozen destination");
        var categoryId = await SeedCategoryAsync(subject, "Frozen purchase");
        var charge = await RecordChargeAsync(client, card, categoryId, 100m);
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        var transferResponse = await client.PostAsJsonAsync("/api/transfers", new
        {
            OriginFinancialAccountId = origin,
            DestinationStatementId = charge.StatementId,
            Amount = 100m,
            OccurredOn = Today
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferEnvelope>())!
            .Data!;
        var read = await client.GetFromJsonAsync<TransferEnvelope>(
            $"/api/transfers/{transfer.Id}");

        var response = await client.DeleteAsync($"/api/transfers/{transfer.Id}");

        Assert.Equal(card, read?.Data?.DestinationCreditCardId);
        Assert.Equal(charge.StatementId, read?.Data?.DestinationStatementId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(TransferMessages.SettledStatementFrozen,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False((await context.Transfers.SingleAsync(item =>
            item.PublicId == transfer.Id)).IsDeleted);
        Assert.All(await context.FinancialTransactions
            .Where(item => item.PublicId == transfer.OutboundTransactionId ||
                item.PublicId == transfer.InboundTransactionId)
            .ToArrayAsync(), item => Assert.False(item.IsDeleted));
    }

    [FunctionalFact]
    public async Task GivenForeignMissingOrUnauthorizedTransfer_WhenManaged_ThenAccessIsHidden()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(owner, "Private origin", "BRL", 100m);
        var destination = await CreateAccountAsync(owner, "Private destination", "BRL", 0m);
        var transfer = await RecordTransferAsync(owner, origin, destination, 10m);
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreignRead = await other.GetAsync($"/api/transfers/{transfer.Id}");
        var foreignDelete = await other.DeleteAsync($"/api/transfers/{transfer.Id}");
        var missingRestore = await other.PostAsync(
            $"/api/transfers/{Guid.NewGuid()}/restore",
            null);
        var anonymousRead = await anonymous.GetAsync($"/api/transfers/{transfer.Id}");
        var administratorDelete = await administrator.DeleteAsync(
            $"/api/transfers/{transfer.Id}");

        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRestore.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDelete.StatusCode);
        await using var context = CreateContext();
        Assert.False((await context.Transfers.SingleAsync(item =>
            item.PublicId == transfer.Id)).IsDeleted);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<decimal> BalanceAsync(HttpClient client, Guid accountId)
    {
        var result = await client.GetFromJsonAsync<BalanceEnvelope>(
            $"/api/accounts/{accountId}/balance?asOf={Today:yyyy-MM-dd}");
        return result!.Data!.Balance;
    }

    private static async Task<TransferData> RecordTransferAsync(
        HttpClient client,
        Guid origin,
        Guid destination,
        decimal amount)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transfers",
            Request(origin, destination, amount));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferEnvelope>())!.Data!;
    }

    private async Task SeedRateAsync(
        string baseCode,
        string quoteCode,
        decimal rate,
        DateOnly rateDate)
    {
        await using var context = CreateContext();
        var baseCurrency = await context.Currencies.SingleAsync(item => item.Code == baseCode);
        var quoteCurrency = await context.Currencies.SingleAsync(item => item.Code == quoteCode);
        context.ExchangeRates.Add(new ExchangeRate(
            baseCurrency.Id,
            quoteCurrency.Id,
            rate,
            rateDate,
            ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

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

    private static async Task<ChargeData> RecordChargeAsync(
        HttpClient client,
        Guid cardId,
        Guid categoryId,
        decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            CreditCardId = cardId,
            CategoryId = categoryId,
            Direction = TransactionDirection.Expense,
            Amount = amount,
            OccurredOn = Today
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChargeEnvelope>())!.Data!;
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 25,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        string currencyCode,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = "Example Bank",
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = currencyCode,
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!.Id;
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

    private static object Request(
        Guid origin,
        Guid destination,
        decimal amount) => new
        {
            OriginFinancialAccountId = origin,
            DestinationFinancialAccountId = destination,
            Amount = amount,
            OccurredOn = Today
        };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Transfer Owner"
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

    private sealed record TransferEnvelope(
        TransferData? Data,
        IReadOnlyCollection<string> Messages);
    private sealed record TransferData(
        Guid Id,
        Guid OutboundTransactionId,
        Guid InboundTransactionId,
        Guid OriginFinancialAccountId,
        Guid? DestinationFinancialAccountId,
        Guid? DestinationCreditCardId,
        Guid? DestinationStatementId,
        decimal OutboundAmount,
        string OutboundCurrencyCode,
        decimal InboundAmount,
        string InboundCurrencyCode,
        decimal? AppliedRate,
        DateOnly? RateDate,
        bool OutboundIsDeleted = false,
        bool InboundIsDeleted = false,
        bool IsDeleted = false);
    private sealed record SearchEnvelope(SearchData? Data);
    private sealed record SearchData(
        IReadOnlyCollection<TransactionData> Items,
        TotalsData Totals);
    private sealed record TransactionData(Guid Id, bool IsTransfer);
    private sealed record TotalsData(IReadOnlyCollection<CurrencyTotalData> ByCurrency);
    private sealed record CurrencyTotalData(string CurrencyCode);
    private sealed record BalanceEnvelope(BalanceData? Data);
    private sealed record BalanceData(decimal Balance);
    private sealed record AccountEnvelope(AccountData? Data);
    private sealed record AccountData(Guid Id);
    private sealed record CardEnvelope(CardData? Data);
    private sealed record CardData(Guid Id);
    private sealed record ChargeEnvelope(ChargeData? Data);
    private sealed record ChargeData(Guid StatementId);
}
