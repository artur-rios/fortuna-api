using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Cards;
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

public sealed class CardChargeAssignmentTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenChargesInSameCycle_WhenRecorded_ThenStatementIsReusedAndTotaled()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Rewards", 20, 5);

        var first = await RecordAsync(client, card.Id, 40m, new DateOnly(2026, 9, 10));
        var second = await RecordAsync(client, card.Id, 60m, new DateOnly(2026, 9, 15));

        Assert.Equal(first.StatementId, second.StatementId);
        Assert.Equal(100m, second.StatementPurchaseTotal);
        Assert.Equal("Open", second.StatementStatus);
        Assert.False(second.IsLateArriving);
        await using var context = CreateContext();
        Assert.Equal(1, await context.CreditCardStatements.CountAsync());
        Assert.Equal(2, await context.FinancialTransactions.CountAsync(item =>
            item.StatementId != null));
        Assert.Contains(await context.AuditEntries.ToArrayAsync(), item =>
            item.Operation == nameof(RecordCardChargeCommand));
    }

    [FunctionalFact]
    public async Task GivenSettledCycle_WhenChargeArrives_ThenNextOpenStatementReceivesLateCharge()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Settled", 20, 5);
        var original = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));
        await SetStatementStatusAsync(original.StatementId, settled: true);

        var late = await RecordAsync(client, card.Id, 25m, new DateOnly(2026, 9, 12));

        Assert.NotEqual(original.StatementId, late.StatementId);
        Assert.True(late.IsLateArriving);
        Assert.Equal(new DateOnly(2026, 9, 21), late.StatementPeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 20), late.StatementPeriodEnd);
        Assert.Equal(25m, late.StatementPurchaseTotal);
        await using var context = CreateContext();
        var settled = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == original.StatementId);
        Assert.Equal(CreditCardStatementStatus.Settled, settled.Status);
        Assert.Equal(100m, settled.PurchaseTotal);
    }

    [FunctionalFact]
    public async Task GivenClosedUnsettledCycle_WhenChargeArrives_ThenSameStatementIsRecomputed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Closed", 20, 5);
        var original = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 9, 10));
        await SetStatementStatusAsync(original.StatementId, settled: false);

        var added = await RecordAsync(client, card.Id, 15m, new DateOnly(2026, 9, 11));

        Assert.Equal(original.StatementId, added.StatementId);
        Assert.Equal("Closed", added.StatementStatus);
        Assert.Equal(25m, added.StatementPurchaseTotal);
        Assert.False(added.IsLateArriving);
    }

    [FunctionalFact]
    public async Task GivenChargeBeforeEarliestCycle_WhenRecorded_ThenEarlierStatementIsOpened()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Backdated", 20, 5);
        var current = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 10, 10));

        var earlier = await RecordAsync(client, card.Id, 20m, new DateOnly(2026, 8, 10));

        Assert.True(earlier.StatementPeriodStart < current.StatementPeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 21), earlier.StatementPeriodStart);
        Assert.Equal(new DateOnly(2026, 8, 20), earlier.StatementPeriodEnd);
        await using var context = CreateContext();
        Assert.Equal(2, await context.CreditCardStatements.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenClosingDayBeyondFebruary_WhenChargeRecorded_ThenMonthEndClosesCycle()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Month End", 31, 5);

        var charge = await RecordAsync(client, card.Id, 20m, new DateOnly(2027, 2, 20));

        Assert.Equal(new DateOnly(2027, 2, 1), charge.StatementPeriodStart);
        Assert.Equal(new DateOnly(2027, 2, 28), charge.StatementClosingDate);
        Assert.Equal(new DateOnly(2027, 3, 5), charge.StatementDueDate);
    }

    [FunctionalFact]
    public async Task GivenInvalidCharge_WhenRecorded_ThenBadRequestNamesFields()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            CreditCardId = Guid.Empty,
            Amount = 0m,
            OccurredOn = default(DateOnly)
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(TransactionMessages.CreditCardIdRequired, body, StringComparison.Ordinal);
        Assert.Contains(TransactionMessages.AmountPositive, body, StringComparison.Ordinal);
        Assert.Contains(TransactionMessages.OccurredOnRequired, body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrDeletedCard_WhenChargeRecorded_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var foreignCard = await CreateCardAsync(owner, "Foreign", 20, 5);
        var deletedCard = await CreateCardAsync(owner, "Deleted", 20, 5);
        (await owner.DeleteAsync($"/api/credit-cards/{deletedCard.Id}")).EnsureSuccessStatusCode();
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var foreign = await other.PostAsJsonAsync("/api/transactions",
            Charge(foreignCard.Id, 10m, new DateOnly(2026, 9, 1)));
        var missing = await other.PostAsJsonAsync("/api/transactions",
            Charge(Guid.NewGuid(), 10m, new DateOnly(2026, 9, 1)));
        var deleted = await owner.PostAsJsonAsync("/api/transactions",
            Charge(deletedCard.Id, 10m, new DateOnly(2026, 9, 1)));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenChargeRecorded_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var body = Charge(Guid.NewGuid(), 10m, new DateOnly(2026, 9, 1));

        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/transactions", body);
        var administratorResponse = await administrator.PostAsJsonAsync("/api/transactions", body);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenCardStatements_WhenCardLifecycleRuns_ThenOnlyCascadeDeletedStatementRestores()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Lifecycle", 20, 5);
        var first = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 8, 10));
        var second = await RecordAsync(client, card.Id, 20m, new DateOnly(2026, 9, 10));
        await using (var context = CreateContext())
        {
            var preDeleted = await context.CreditCardStatements.SingleAsync(item =>
                item.PublicId == first.StatementId);
            preDeleted.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        (await client.DeleteAsync($"/api/credit-cards/{card.Id}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/credit-cards/{card.Id}/restore", null))
            .EnsureSuccessStatusCode();

        await using var assertionContext = CreateContext();
        var statements = await assertionContext.CreditCardStatements
            .Where(item => item.CreditCard.PublicId == card.Id)
            .ToArrayAsync();
        Assert.True(statements.Single(item => item.PublicId == first.StatementId).IsDeleted);
        Assert.False(statements.Single(item => item.PublicId == second.StatementId).IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenLiveCharges_WhenStatementClosed_ThenLiveTotalAndDueDateAreReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Closing", 20, 5);
        var live = await RecordAsync(client, card.Id, 70m, new DateOnly(2026, 9, 10));
        var deleted = await RecordAsync(client, card.Id, 30m, new DateOnly(2026, 9, 11));
        await using (var context = CreateContext())
        {
            var transaction = await context.FinancialTransactions.SingleAsync(item =>
                item.PublicId == deleted.Id);
            transaction.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/statements/{live.StatementId}/close", null);
        var result = await response.Content.ReadFromJsonAsync<StatementEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Closed", result?.Data?.Status);
        Assert.Equal(70m, result?.Data?.PurchaseTotal);
        Assert.Equal(70m, result?.Data?.AmountDue);
        Assert.Equal(new DateOnly(2026, 10, 5), result?.Data?.DueDate);
    }

    [FunctionalFact]
    public async Task GivenFutureOpenStatement_WhenAutomaticCloseRuns_ThenItRemainsOpen()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, subject, HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Future", 20, 5);
        var charge = await RecordAsync(client, card.Id, 10m, new DateOnly(2030, 9, 10));
        await using var context = CreateContext();
        var userId = await context.UserProfiles
            .Where(item => item.ExternalSubject == subject.ToString("D"))
            .Select(item => item.PublicId)
            .SingleAsync();
        var result = await new EfCreditCardStatementStore(context).CloseAsync(
            userId,
            charge.StatementId,
            new DateOnly(2030, 9, 20),
            explicitRequest: false,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(CreditCardStatementCloseOutcome.NotDue, result.Outcome);
        Assert.Equal("Open", result.Statement?.Status);
    }

    [FunctionalFact]
    public async Task GivenClosedUnsettledStatement_WhenClosedAgain_ThenTotalIsRecomputed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Reclose", 20, 5);
        var first = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 9, 10));
        (await client.PostAsync($"/api/statements/{first.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        await RecordAsync(client, card.Id, 15m, new DateOnly(2026, 9, 11));

        var response = await client.PostAsync($"/api/statements/{first.StatementId}/close", null);
        var result = await response.Content.ReadFromJsonAsync<StatementEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(25m, result?.Data?.PurchaseTotal);
        Assert.Equal("Closed", result?.Data?.Status);
    }

    [FunctionalFact]
    public async Task GivenSettledStatement_WhenClosedAgain_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Frozen", 20, 5);
        var charge = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 9, 10));
        await SetStatementStatusAsync(charge.StatementId, settled: true);

        var response = await client.PostAsync($"/api/statements/{charge.StatementId}/close", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(CreditCardStatementMessages.SettledStatementFrozen,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenEmptyStatement_WhenExplicitlyClosed_ThenTotalIsZero()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var cardData = await CreateCardAsync(client, "Empty", 20, 5);
        Guid statementId;
        await using (var context = CreateContext())
        {
            var card = await context.CreditCards.SingleAsync(item => item.PublicId == cardData.Id);
            var statement = new CreditCardStatement(
                card,
                BillingCycle.Containing(new DateOnly(2026, 9, 10), 20, 5),
                DateTimeOffset.UtcNow);
            context.CreditCardStatements.Add(statement);
            await context.SaveChangesAsync();
            statementId = statement.PublicId;
        }

        var response = await client.PostAsync($"/api/statements/{statementId}/close", null);
        var result = await response.Content.ReadFromJsonAsync<StatementEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, result?.Data?.AmountDue);
        Assert.Equal("Closed", result?.Data?.Status);
    }

    [FunctionalFact]
    public async Task GivenLiveDeletedLateAndForeignCharges_WhenStatementRead_ThenInvoiceIsComplete()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Invoice", 20, 5);
        var live = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));
        var foreign = await RecordAsync(client, card.Id, 50m, new DateOnly(2026, 9, 11));
        var deleted = await RecordAsync(client, card.Id, 25m, new DateOnly(2026, 9, 12));
        await using (var context = CreateContext())
        {
            var statement = await context.CreditCardStatements
                .Include(item => item.CreditCard)
                .SingleAsync(item => item.PublicId == live.StatementId);
            var foreignTransaction = await context.FinancialTransactions
                .Include(item => item.CreditCard)
                .ThenInclude(item => item!.Currency)
                .SingleAsync(item => item.PublicId == foreign.Id);
            var deletedTransaction = await context.FinancialTransactions
                .SingleAsync(item => item.PublicId == deleted.Id);
            var usd = await context.Currencies.SingleAsync(item => item.Code == "USD");
            foreignTransaction.AssignToStatement(statement, true, DateTimeOffset.UtcNow);
            foreignTransaction.RecordForeignCurrencyDetails(
                10m,
                usd,
                5m,
                new DateOnly(2026, 9, 10),
                DateTimeOffset.UtcNow);
            deletedTransaction.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/statements/{live.StatementId}");
        var result = await response.Content.ReadFromJsonAsync<StatementReadEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(card.Id, result?.Data?.CreditCardId);
        Assert.Equal("BRL", result?.Data?.CurrencyCode);
        Assert.Equal(150m, result?.Data?.PurchaseTotal);
        Assert.Equal(150m, result?.Data?.AmountDue);
        Assert.Equal(2, result?.Data?.Transactions.Count);
        var foreignCharge = result!.Data!.Transactions.Single(item => item.Id == foreign.Id);
        Assert.True(foreignCharge.IsLateArriving);
        Assert.Equal(10m, foreignCharge.OriginalAmount);
        Assert.Equal("USD", foreignCharge.OriginalCurrencyCode);
        Assert.Equal(5m, foreignCharge.AppliedRate);
        Assert.Equal(new DateOnly(2026, 9, 10), foreignCharge.RateDate);
        Assert.DoesNotContain(result.Data.Transactions, item => item.Id == deleted.Id);
        Assert.Contains(CreditCardStatementMessages.RetrievedSuccessfully, result.Messages);
    }

    [FunctionalFact]
    public async Task GivenForeignStatementOrCard_WhenViewed_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(owner, "Private statement", 20, 5);
        var charge = await RecordAsync(owner, card.Id, 10m, new DateOnly(2026, 9, 10));
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var detail = await other.GetAsync($"/api/statements/{charge.StatementId}");
        var list = await other.GetAsync($"/api/credit-cards/{card.Id}/statements");

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Contains(CreditCardStatementMessages.NotFound,
            await detail.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains(CreditCardStatementMessages.CreditCardNotFound,
            await list.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenFilteredStatements_WhenListed_ThenTheyAreSortedAndPaginated()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Statement history", 20, 5);
        var august = await RecordAsync(client, card.Id, 10m, new DateOnly(2026, 8, 10));
        var september = await RecordAsync(client, card.Id, 30m, new DateOnly(2026, 9, 10));
        await RecordAsync(client, card.Id, 50m, new DateOnly(2026, 10, 10));
        (await client.PostAsync($"/api/statements/{august.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/statements/{september.StatementId}/close", null))
            .EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/credit-cards/{card.Id}/statements?Status=Closed" +
            "&From=2026-07-01&To=2026-09-20&SortBy=AmountDue" +
            "&Descending=true&PageNumber=1&PageSize=1");
        var page = await response.Content.ReadFromJsonAsync<StatementReadPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page?.TotalItems);
        Assert.Equal(1, page?.PageSize);
        Assert.Equal(30m, Assert.Single(page!.Data).AmountDue);
        Assert.Contains(CreditCardStatementMessages.ListedSuccessfully, page.Messages);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrUnsupportedStatementFilter_WhenListed_ThenBadRequestNamesIt()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Invalid filters", 20, 5);

        var invalid = await client.GetAsync(
            $"/api/credit-cards/{card.Id}/statements?PageNumber=0" +
            "&Status=Closed&From=2026-10-01&To=2026-09-01&SortBy=Balance");
        var invalidStatus = await client.GetAsync(
            $"/api/credit-cards/{card.Id}/statements?Status=999");
        var unsupported = await client.GetAsync(
            $"/api/credit-cards/{card.Id}/statements?CurrencyCode=BRL");
        var invalidBody = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains(CreditCardStatementMessages.InvalidPageNumber, invalidBody);
        Assert.Contains(CreditCardStatementMessages.PeriodInvalid, invalidBody);
        Assert.Contains(CreditCardStatementMessages.SortByUnsupported, invalidBody);
        Assert.Contains("Status", await invalidStatus.Content.ReadAsStringAsync());
        Assert.Contains("CurrencyCode", await unsupported.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenStatementsViewed_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var anonymousDetail = await anonymous.GetAsync($"/api/statements/{id}");
        var anonymousList = await anonymous.GetAsync($"/api/credit-cards/{id}/statements");
        var administratorDetail = await administrator.GetAsync($"/api/statements/{id}");
        var administratorList = await administrator.GetAsync(
            $"/api/credit-cards/{id}/statements");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorList.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenClosedStatement_WhenPaidInFull_ThenPairedTransferSettlesIt()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Checking", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Paid in full", 20, 5);
        var charge = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();

        var response = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            100m,
            new DateOnly(2026, 9, 25));

        Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
        Assert.Equal("Settled", response.Data.Status);
        Assert.Equal(0m, response.Data.RemainingBalance);
        Assert.Equal(0m, response.Data.CreditAmount);
        Assert.Null(response.Data.CarryStatementId);
        var accountBalance = await client.GetFromJsonAsync<AccountBalanceEnvelope>(
            $"/api/accounts/{account.Id}/balance?asOf=2026-09-25");
        var cardBalance = await client.GetFromJsonAsync<CreditCardBalanceEnvelope>(
            $"/api/credit-cards/{card.Id}");
        Assert.Equal(900m, accountBalance?.Data?.Balance);
        Assert.Equal(0m, cardBalance?.Data?.UsedAmount);
        await using var context = CreateContext();
        var transfer = await context.Transfers.SingleAsync(item =>
            item.PublicId == response.Data.TransferId);
        Assert.Equal(response.Data.OutboundTransactionId,
            (await context.FinancialTransactions.SingleAsync(item =>
                item.Id == transfer.OutboundTransactionId)).PublicId);
        var statement = await context.CreditCardStatements.SingleAsync(item =>
            item.PublicId == charge.StatementId);
        Assert.Equal(CreditCardStatementStatus.Settled, statement.Status);
    }

    [FunctionalFact]
    public async Task GivenOpenOrSettledStatement_WhenPaid_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Conflict account", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Conflict card", 20, 5);
        var charge = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));

        var open = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            100m,
            new DateOnly(2026, 9, 25));
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        var settled = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            100m,
            new DateOnly(2026, 9, 25));
        var repeated = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            100m,
            new DateOnly(2026, 9, 25));

        Assert.Equal(HttpStatusCode.Conflict, open.Response.StatusCode);
        Assert.Contains(CreditCardStatementMessages.StatementOpen,
            await open.Response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, settled.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.Response.StatusCode);
        Assert.Contains(CreditCardStatementMessages.StatementAlreadySettled,
            await repeated.Response.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenPartialPayment_WhenSettled_ThenRemainderCarriesToNextStatement()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Partial account", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Partial card", 20, 5);
        var charge = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();

        var settlement = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            40m,
            new DateOnly(2026, 9, 25));
        var carry = await client.GetFromJsonAsync<StatementReadEnvelope>(
            $"/api/statements/{settlement.Data.CarryStatementId}");

        Assert.Equal(60m, settlement.Data.RemainingBalance);
        Assert.Equal(60m, carry?.Data?.PreviousBalance);
        Assert.Equal(60m, carry?.Data?.AmountDue);
        Assert.Empty(carry!.Data!.Transactions);
    }

    [FunctionalFact]
    public async Task GivenOverpayment_WhenSettled_ThenExcessIsCardCredit()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Credit account", "BRL", 1000m);
        var card = await CreateCardAsync(client, "Credit card", 20, 5);
        var charge = await RecordAsync(client, card.Id, 100m, new DateOnly(2026, 9, 10));
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();

        var settlement = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            125m,
            new DateOnly(2026, 9, 25));

        Assert.Equal(25m, settlement.Data.CreditAmount);
        Assert.Equal(0m, settlement.Data.RemainingBalance);
        Assert.Null(settlement.Data.CarryStatementId);
        var cardBalance = await client.GetFromJsonAsync<CreditCardBalanceEnvelope>(
            $"/api/credit-cards/{card.Id}");
        Assert.Equal(0m, cardBalance?.Data?.UsedAmount);
    }

    [FunctionalFact]
    public async Task GivenForeignPayingAccount_WhenSettled_ThenRateAndDateAreRecorded()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Dollar account", "USD", 1000m);
        var card = await CreateCardAsync(client, "Foreign payment", 20, 5);
        var charge = await RecordAsync(client, card.Id, 500m, new DateOnly(2026, 9, 10));
        (await client.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        await using (var context = CreateContext())
        {
            var usd = await context.Currencies.SingleAsync(item => item.Code == "USD");
            var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
            context.ExchangeRates.Add(new ExchangeRate(
                usd.Id,
                brl.Id,
                5m,
                new DateOnly(2026, 9, 24),
                ExchangeRateSource.Manual));
            await context.SaveChangesAsync();
        }

        var settlement = await SettleAsync(
            client,
            charge.StatementId,
            account.Id,
            100m,
            new DateOnly(2026, 9, 25));

        Assert.Equal(500m, settlement.Data.AppliedAmount);
        Assert.Equal("USD", settlement.Data.PaymentCurrencyCode);
        Assert.Equal("BRL", settlement.Data.CreditCardCurrencyCode);
        Assert.Equal(5m, settlement.Data.AppliedRate);
        Assert.Equal(new DateOnly(2026, 9, 24), settlement.Data.RateDate);
        await using var assertionContext = CreateContext();
        var transfer = await assertionContext.Transfers.SingleAsync(item =>
            item.PublicId == settlement.Data.TransferId);
        Assert.Equal(5m, transfer.AppliedRate);
        var inbound = await assertionContext.FinancialTransactions
            .Include(item => item.OriginalCurrency)
            .SingleAsync(item => item.PublicId == settlement.Data.InboundTransactionId);
        Assert.Equal(100m, inbound.OriginalAmount);
        Assert.Equal("USD", inbound.OriginalCurrency?.Code);
    }

    [FunctionalFact]
    public async Task GivenForeignOrDeletedPayingAccount_WhenSettled_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(owner, "Owned statement", 20, 5);
        var charge = await RecordAsync(owner, card.Id, 100m, new DateOnly(2026, 9, 10));
        (await owner.PostAsync($"/api/statements/{charge.StatementId}/close", null))
            .EnsureSuccessStatusCode();
        var deleted = await CreateAccountAsync(owner, "Deleted payer", "BRL", 1000m);
        (await owner.DeleteAsync($"/api/accounts/{deleted.Id}")).EnsureSuccessStatusCode();
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreign = await CreateAccountAsync(other, "Foreign payer", "BRL", 1000m);

        var deletedResult = await SettleAsync(
            owner, charge.StatementId, deleted.Id, 100m, new DateOnly(2026, 9, 25));
        var foreignResult = await SettleAsync(
            owner, charge.StatementId, foreign.Id, 100m, new DateOnly(2026, 9, 25));

        Assert.Equal(HttpStatusCode.NotFound, deletedResult.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResult.Response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrUnauthorizedSettlement_WhenPosted_ThenItIsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var invalid = await client.PostAsJsonAsync($"/api/statements/{Guid.NewGuid()}/settle", new
        {
            FinancialAccountId = Guid.Empty,
            Amount = 0m,
            PaymentDate = default(DateOnly)
        });
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var body = new
        {
            FinancialAccountId = Guid.NewGuid(),
            Amount = 1m,
            PaymentDate = new DateOnly(2026, 9, 25)
        };
        var anonymousResult = await anonymous.PostAsJsonAsync(
            $"/api/statements/{Guid.NewGuid()}/settle",
            body);
        var administratorResult = await administrator.PostAsJsonAsync(
            $"/api/statements/{Guid.NewGuid()}/settle",
            body);
        var invalidBody = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(CreditCardStatementMessages.FinancialAccountIdRequired, invalidBody);
        Assert.Contains(CreditCardStatementMessages.PaymentAmountPositive, invalidBody);
        Assert.Contains(CreditCardStatementMessages.PaymentDateRequired, invalidBody);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResult.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResult.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task SetStatementStatusAsync(Guid statementId, bool settled)
    {
        await using var context = CreateContext();
        var statement = await context.CreditCardStatements
            .Include(item => item.CreditCard)
            .ThenInclude(item => item.User)
            .SingleAsync(item => item.PublicId == statementId);
        statement.Close(DateTimeOffset.UtcNow);
        if (settled)
        {
            var settlement = new FinancialTransaction(
                statement.CreditCard.User,
                statement.CreditCard,
                TransactionDirection.Earning,
                statement.AmountDue,
                statement.DueDate,
                DateTimeOffset.UtcNow);
            context.FinancialTransactions.Add(settlement);
            await context.SaveChangesAsync();
            statement.Settle(settlement, DateTimeOffset.UtcNow);
        }

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

    private static async Task<ChargeData> RecordAsync(
        HttpClient client,
        Guid cardId,
        decimal amount,
        DateOnly occurredOn)
    {
        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            Charge(cardId, amount, occurredOn));
        var envelope = await response.Content.ReadFromJsonAsync<ChargeEnvelope>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(TransactionMessages.CardChargeCreatedSuccessfully, envelope!.Messages);
        return envelope.Data!;
    }

    private static object Charge(Guid cardId, decimal amount, DateOnly occurredOn) => new
    {
        CreditCardId = cardId,
        Amount = amount,
        OccurredOn = occurredOn
    };

    private static async Task<CardData> CreateCardAsync(
        HttpClient client,
        string name,
        short closingDay,
        short dueDay)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = closingDay,
            DueDay = dueDay,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardEnvelope>())!.Data!;
    }

    private static async Task<AccountData> CreateAccountAsync(
        HttpClient client,
        string name,
        string currencyCode,
        decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            Institution = "Example Bank",
            AccountType = 1,
            CurrencyCode = currencyCode,
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!;
    }

    private static async Task<SettlementResponse> SettleAsync(
        HttpClient client,
        Guid statementId,
        Guid accountId,
        decimal amount,
        DateOnly paymentDate)
    {
        var response = await client.PostAsJsonAsync($"/api/statements/{statementId}/settle", new
        {
            FinancialAccountId = accountId,
            Amount = amount,
            PaymentDate = paymentDate
        });
        var envelope = await response.Content.ReadFromJsonAsync<SettlementEnvelope>();
        return new SettlementResponse(response, envelope?.Data!);
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
    private sealed record AccountEnvelope(AccountData? Data);
    private sealed record AccountData(Guid Id);
    private sealed record AccountBalanceEnvelope(AccountBalanceData? Data);
    private sealed record AccountBalanceData(decimal Balance);
    private sealed record CreditCardBalanceEnvelope(CreditCardBalanceData? Data);
    private sealed record CreditCardBalanceData(decimal UsedAmount);
    private sealed record ChargeEnvelope(ChargeData? Data, IReadOnlyList<string> Messages);
    private sealed record ChargeData(
        Guid Id,
        Guid CreditCardId,
        decimal Amount,
        DateOnly OccurredOn,
        bool IsLateArriving,
        Guid StatementId,
        DateOnly StatementPeriodStart,
        DateOnly StatementPeriodEnd,
        DateOnly StatementClosingDate,
        DateOnly StatementDueDate,
        string StatementStatus,
        decimal StatementPurchaseTotal);
    private sealed record StatementEnvelope(StatementData? Data);
    private sealed record StatementData(
        Guid Id,
        Guid CreditCardId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        DateOnly ClosingDate,
        DateOnly DueDate,
        string Status,
        decimal PurchaseTotal,
        decimal AmountDue);
    private sealed record StatementReadEnvelope(
        StatementReadData? Data,
        IReadOnlyList<string> Messages);
    private sealed record StatementReadPage(
        List<StatementReadData> Data,
        int PageNumber,
        int PageSize,
        int TotalItems,
        IReadOnlyList<string> Messages);
    private sealed record StatementReadData(
        Guid Id,
        Guid CreditCardId,
        string CurrencyCode,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        DateOnly ClosingDate,
        DateOnly DueDate,
        decimal PreviousBalance,
        decimal PaymentsReceived,
        decimal PurchaseTotal,
        decimal ForeignTaxTotal,
        decimal OtherEntries,
        decimal AmountDue,
        string Status,
        Guid? SettlementTransactionId,
        List<StatementTransactionData> Transactions);
    private sealed record StatementTransactionData(
        Guid Id,
        string Direction,
        decimal Amount,
        DateOnly OccurredOn,
        bool IsLateArriving,
        decimal? OriginalAmount,
        string? OriginalCurrencyCode,
        decimal? AppliedRate,
        DateOnly? RateDate);
    private sealed record SettlementResponse(HttpResponseMessage Response, SettlementData Data);
    private sealed record SettlementEnvelope(SettlementData? Data);
    private sealed record SettlementData(
        Guid Id,
        string Status,
        Guid TransferId,
        Guid OutboundTransactionId,
        Guid InboundTransactionId,
        Guid FinancialAccountId,
        decimal PaymentAmount,
        string PaymentCurrencyCode,
        decimal AppliedAmount,
        string CreditCardCurrencyCode,
        decimal StatementAmountDue,
        decimal RemainingBalance,
        Guid? CarryStatementId,
        decimal CreditAmount,
        decimal? AppliedRate,
        DateOnly? RateDate,
        DateOnly PaymentDate);
}
