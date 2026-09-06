using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
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

public sealed class ConnectionCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private const string AccessToken = "pluggy-access-token-never-stored-in-plain-text";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 7, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenValidPluggyItem_WhenConnected_ThenOnlyEncryptedTokenIsStored()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await ConnectAsync(client);
        var connection = (await response.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(TransactionSourceType.Pluggy, connection.DataSourceType);
        Assert.Equal("Nubank", connection.Institution);
        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.DoesNotContain(AccessToken, await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.Connections.SingleAsync();
        Assert.NotEmpty(stored.AccessTokenCipher);
        Assert.NotEqual(AccessToken, System.Text.Encoding.UTF8.GetString(stored.AccessTokenCipher));
        Assert.NotEmpty(await context.DataProtectionKeys.ToListAsync());
    }

    [FunctionalTheory]
    [InlineData(PluggyConnectionValidationOutcome.InvalidReference, HttpStatusCode.BadRequest,
        ConnectionMessages.InvalidReference)]
    [InlineData(PluggyConnectionValidationOutcome.Unavailable, HttpStatusCode.ServiceUnavailable,
        ConnectionMessages.SourceUnavailable)]
    [InlineData(PluggyConnectionValidationOutcome.NotConfigured, HttpStatusCode.NotFound,
        ConnectionMessages.SourceNotAvailable)]
    public async Task GivenPluggyRejection_WhenConnected_ThenNothingIsStored(
        PluggyConnectionValidationOutcome outcome,
        HttpStatusCode expectedStatus,
        string expectedMessage)
    {
        await using var factory = CreateFactory(Gateway(outcome));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await ConnectAsync(client);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Connections.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenCredentialInRequest_WhenConnected_ThenItIsRejectedBeforeExternalCall()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/connections", new
        {
            DataSource = "pluggy",
            ExternalReference = Guid.NewGuid(),
            Username = "customer",
            Password = "bank-password",
            MfaToken = "123456"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ConnectionMessages.BankCredentialRejected,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, gateway.CallCount);
        await using var context = CreateContext();
        Assert.False(await context.Connections.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenSameReference_WhenConnected_ThenDuplicateIsScopedToOwner()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var reference = Guid.NewGuid();

        var created = await ConnectAsync(owner, reference);
        var duplicate = await ConnectAsync(owner, reference);
        var isolated = await ConnectAsync(other, reference);
        var existing = (await duplicate.Content
            .ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.NotEqual(Guid.Empty, existing.Id);
        Assert.Equal(HttpStatusCode.Created, isolated.StatusCode);
        Assert.Equal(3, gateway.CallCount);
        await using var context = CreateContext();
        Assert.Equal(2, await context.Connections.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenConnected_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var unauthorized = await ConnectAsync(anonymous);
        var forbidden = await ConnectAsync(administrator);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenOwnedConnection_WhenSynchronized_ThenPendingImportJobIsReturned()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var response = await client.PostAsJsonAsync($"/api/connections/{connection.Id}/sync", new
        {
            PeriodStart = new DateOnly(2026, 8, 1),
            PeriodEnd = new DateOnly(2026, 8, 31)
        });
        var body = (await response.Content.ReadFromJsonAsync<SynchronizationEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEqual(Guid.Empty, body.ImportJobId);
        Assert.Equal(ImportJobStatus.Pending, body.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), body.PeriodStart);
        await using var context = CreateContext();
        Assert.Equal(1, await context.ImportJobs.CountAsync(
            item => item.PublicId == body.ImportJobId));
        Assert.True(await context.BackgroundJobs.AnyAsync(
            item => item.Type == PluggySynchronizationJob.Type));
    }

    [FunctionalFact]
    public async Task GivenRunningSynchronization_WhenRequestedAgain_ThenExistingJobConflicts()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var first = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });
        var duplicate = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });
        var firstJob = (await first.Content.ReadFromJsonAsync<SynchronizationEnvelope>())!.Data!;
        var existing = (await duplicate.Content.ReadFromJsonAsync<SynchronizationEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(firstJob.ImportJobId, existing.ImportJobId);
        Assert.Contains(PluggySynchronizationMessages.AlreadyRunning,
            await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAnotherOwnersConnection_WhenSynchronized_ThenItIsNotFound()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(owner);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var response = await other.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(PluggySynchronizationMessages.ConnectionNotFound,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenInactiveConnection_WhenSynchronized_ThenConflictExplainsState()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await using (var context = CreateContext())
        {
            var stored = await context.Connections.SingleAsync(
                item => item.PublicId == connection.Id);
            stored.MarkRequiresReauthentication(Now.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ConnectionMessages.RequiresReauthentication,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenInvalidSynchronizationPeriod_WhenRequested_ThenItIsRejected()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var response = await client.PostAsJsonAsync($"/api/connections/{connection.Id}/sync", new
        {
            PeriodStart = new DateOnly(2026, 9, 2),
            PeriodEnd = new DateOnly(2026, 9, 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PluggySynchronizationMessages.PeriodInvalid,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.ImportJobs.AnyAsync(
            item => item.Connection!.PublicId == connection.Id));
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenSynchronizing_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var unauthorized = await anonymous.PostAsJsonAsync(
            $"/api/connections/{id}/sync", new { });
        var forbidden = await administrator.PostAsJsonAsync(
            $"/api/connections/{id}/sync", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenExpiredConnection_WhenReauthenticated_ThenDataRemainsAndStateIsActive()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        long recordId;
        long transactionId;
        await using (var context = CreateContext())
        {
            var stored = await context.Connections
                .Include(item => item.User)
                .SingleAsync(item => item.PublicId == connection.Id);
            var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
            var account = new FinancialAccount(
                stored.User, $"Imported {Guid.NewGuid():N}", "Nubank",
                FinancialAccountType.Checking, currency, 0, Now);
            var category = new Category(stored.User, $"Food {Guid.NewGuid():N}", Now);
            var job = new ImportJob(stored.User, stored, null, null, Now);
            job.Start(Now.AddMinutes(1));
            var record = new ImportedRecord(
                job, "{\"id\":\"preserved-row\"}", ImportedRecordOutcome.Imported,
                25m, new DateOnly(2026, 9, 1), "preserved-row");
            var transaction = new FinancialTransaction(
                stored.User, account, category, TransactionDirection.Expense,
                25m, new DateOnly(2026, 9, 1), Now, "Preserved purchase");
            transaction.MarkAsImported(record, TransactionSourceType.Pluggy, Now);
            job.Complete(1, 0, 0, Now.AddMinutes(2));
            stored.MarkRequiresReauthentication(Now.AddMinutes(3));
            context.AddRange(account, category, job, record, transaction);
            await context.SaveChangesAsync();
            recordId = record.Id;
            transactionId = transaction.Id;
        }

        var newReference = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/reauthenticate",
            new { ExternalReference = newReference });
        var body = (await response.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(newReference.ToString(), body.ExternalReference);
        Assert.Equal(ConnectionStatus.Active, body.Status);
        Assert.Equal("Nubank", body.Institution);
        await using var assertionContext = CreateContext();
        var updated = await assertionContext.Connections.SingleAsync(
            item => item.PublicId == connection.Id);
        Assert.Equal(newReference.ToString(), updated.ExternalReference);
        Assert.Equal(ConnectionStatus.Active, updated.Status);
        Assert.True(await assertionContext.ImportedRecords.AnyAsync(item => item.Id == recordId));
        Assert.True(await assertionContext.FinancialTransactions.AnyAsync(
            item => item.Id == transactionId && item.ImportedRecordId == recordId));
        var audit = await assertionContext.AuditEntries.SingleAsync(item =>
            item.Operation == "ReauthenticateConnectionCommand" &&
            item.EntityPublicId == connection.Id);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("Connection", audit.EntityType);
    }

    [FunctionalFact]
    public async Task GivenInvalidNewReference_WhenReauthenticated_ThenExpiredStateIsPreserved()
    {
        var gateway = new SequencedPluggyGateway(
            new(PluggyConnectionValidationOutcome.Succeeded, "Nubank", AccessToken),
            new(PluggyConnectionValidationOutcome.InvalidReference));
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await MarkRequiresReauthenticationAsync(connection.Id);
        await using var beforeContext = CreateContext();
        var auditCount = await beforeContext.AuditEntries.CountAsync(item =>
            item.Operation == "ReauthenticateConnectionCommand");

        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/reauthenticate",
            new { ExternalReference = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.Equal(ConnectionStatus.RequiresReauthentication,
            await context.Connections.Where(item => item.PublicId == connection.Id)
                .Select(item => item.Status).SingleAsync());
        Assert.Equal(auditCount + 1, await context.AuditEntries.CountAsync(item =>
            item.Operation == "ReauthenticateConnectionCommand"));
        Assert.Contains(await context.AuditEntries.ToArrayAsync(), item =>
            item.Operation == "ReauthenticateConnectionCommand" &&
            item.Outcome == AuditOutcome.Refused);
    }

    [FunctionalFact]
    public async Task GivenRevokedConnection_WhenReauthenticated_ThenConflictDoesNotCallPluggy()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE fortuna.connection SET status = 3 WHERE public_id = {connection.Id}");
        }

        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/reauthenticate",
            new { ExternalReference = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ConnectionMessages.Revoked,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, gateway.CallCount);
    }

    [FunctionalFact]
    public async Task GivenOwnedConnections_WhenViewed_ThenCurrentStatusesAreSurfaced()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var expiredResponse = await ConnectAsync(owner);
        var activeResponse = await ConnectAsync(owner);
        await ConnectAsync(other);
        var expired = (await expiredResponse.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        var active = (await activeResponse.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await MarkRequiresReauthenticationAsync(expired.Id);

        var detail = await owner.GetFromJsonAsync<ConnectionQueryEnvelope>(
            $"/api/connections/{expired.Id}");
        var page = await owner.GetFromJsonAsync<ConnectionPage>(
            $"/api/connections?Status={(int)ConnectionStatus.RequiresReauthentication}");

        Assert.Equal(ConnectionStatus.RequiresReauthentication, detail?.Data?.Status);
        Assert.Equal(1, page?.TotalItems);
        Assert.Equal(expired.Id, page?.Data.Single().Id);
        Assert.DoesNotContain(page!.Data, item => item.Id == active.Id);
    }

    [FunctionalFact]
    public async Task GivenAnotherOwnersConnection_WhenViewedOrReauthenticated_ThenItIsNotFound()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(owner);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await MarkRequiresReauthenticationAsync(connection.Id);

        var foreignRead = await other.GetAsync($"/api/connections/{connection.Id}");
        var missingRead = await other.GetAsync($"/api/connections/{Guid.NewGuid()}");
        var foreignWrite = await other.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/reauthenticate",
            new { ExternalReference = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        var missingError = await missingRead.Content.ReadFromJsonAsync<ConnectionErrorEnvelope>();
        var foreignError = await foreignRead.Content.ReadFromJsonAsync<ConnectionErrorEnvelope>();
        Assert.Equal(missingError?.Errors, foreignError?.Errors);
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenManagingConnectionState_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var anonymousList = await anonymous.GetAsync("/api/connections");
        var anonymousRead = await anonymous.GetAsync($"/api/connections/{id}");
        var anonymousWrite = await anonymous.PostAsJsonAsync(
            $"/api/connections/{id}/reauthenticate",
            new { ExternalReference = Guid.NewGuid() });
        var forbiddenWrite = await administrator.PostAsJsonAsync(
            $"/api/connections/{id}/reauthenticate",
            new { ExternalReference = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenInvalidReauthenticationBody_WhenSubmitted_ThenFieldsAreRejected()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await MarkRequiresReauthenticationAsync(connection.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/reauthenticate",
            new { ExternalReference = "not-an-item", Password = "bank-secret" });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ConnectionMessages.ExternalReferenceInvalid, text, StringComparison.Ordinal);
        Assert.Contains(ConnectionMessages.BankCredentialRejected, text, StringComparison.Ordinal);
        Assert.Equal(1, gateway.CallCount);
    }

    [FunctionalFact]
    public async Task GivenInvalidConnectionListQuery_WhenViewed_ThenNamedErrorIsReturned()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var invalidPage = await client.GetAsync("/api/connections?PageNumber=0");
        var unsupported = await client.GetAsync("/api/connections?Institution=Bank");

        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Contains(ConnectionMessages.InvalidPageNumber,
            await invalidPage.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains(ConnectionMessages.UnsupportedFilter("Institution"),
            await unsupported.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenImportedHistory_WhenConnectionIsRevoked_ThenTokenIsDiscardedAndDataRemains()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        long recordId;
        long transactionId;
        await using (var context = CreateContext())
        {
            var stored = await context.Connections.Include(item => item.User)
                .SingleAsync(item => item.PublicId == connection.Id);
            var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
            var account = new FinancialAccount(
                stored.User, $"Revoked {Guid.NewGuid():N}", "Nubank",
                FinancialAccountType.Checking, currency, 0, Now);
            var category = new Category(stored.User, $"History {Guid.NewGuid():N}", Now);
            var job = new ImportJob(stored.User, stored, null, null, Now);
            job.Start(Now.AddMinutes(1));
            var record = new ImportedRecord(
                job, "{\"id\":\"retained\"}", ImportedRecordOutcome.Imported,
                40m, new DateOnly(2026, 9, 1), "retained");
            var imported = new FinancialTransaction(
                stored.User, account, category, TransactionDirection.Expense,
                40m, new DateOnly(2026, 9, 1), Now, "Retained history");
            imported.MarkAsImported(record, TransactionSourceType.Pluggy, Now);
            job.Complete(1, 0, 0, Now.AddMinutes(2));
            context.AddRange(account, category, job, record, imported);
            await context.SaveChangesAsync();
            recordId = record.Id;
            transactionId = imported.Id;
        }

        var response = await client.PostAsync(
            $"/api/connections/{connection.Id}/revoke", null);
        var body = (await response.Content.ReadFromJsonAsync<RevocationEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ConnectionStatus.Revoked, body.Status);
        Assert.True(body.ImportedDataRetained);
        Assert.Contains(ConnectionMessages.RevokedSuccessfully,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var assertionContext = CreateContext();
        var revoked = await assertionContext.Connections.SingleAsync(
            item => item.PublicId == connection.Id);
        Assert.Empty(revoked.AccessTokenCipher);
        Assert.True(await assertionContext.ImportedRecords.AnyAsync(item => item.Id == recordId));
        Assert.True(await assertionContext.FinancialTransactions.AnyAsync(
            item => item.Id == transactionId && item.ImportedRecordId == recordId));
        var audit = await assertionContext.AuditEntries.SingleAsync(item =>
            item.Operation == "RevokeConnectionCommand" &&
            item.EntityPublicId == connection.Id);
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal("Connection", audit.EntityType);
    }

    [FunctionalFact]
    public async Task GivenRunningSynchronization_WhenRevoked_ThenJobStopsAndCannotImportLater()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        var queued = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });
        var importJobId = (await queued.Content
            .ReadFromJsonAsync<SynchronizationEnvelope>())!.Data!.ImportJobId;
        await using (var context = CreateContext())
        {
            var job = await context.ImportJobs.SingleAsync(item => item.PublicId == importJobId);
            job.Start(Now.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        var revokedResponse = await client.PostAsync(
            $"/api/connections/{connection.Id}/revoke", null);
        var revoked = (await revokedResponse.Content
            .ReadFromJsonAsync<RevocationEnvelope>())!.Data!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPluggySynchronizationStore>();
            await store.CompleteAsync(
                importJobId,
                new PluggySynchronizationBatch([],
                [
                    new PluggyTransactionRecord(
                        "{\"id\":\"too-late\"}", "missing", "too-late",
                        TransactionDirection.Expense, 10m, new DateOnly(2026, 9, 2),
                        "Too late", "Ignored")
                ]),
                Now.AddMinutes(3),
                CancellationToken.None);
        }

        Assert.Equal(HttpStatusCode.OK, revokedResponse.StatusCode);
        Assert.Equal(1, revoked.StoppedSynchronizations);
        await using var assertionContext = CreateContext();
        var jobState = await assertionContext.ImportJobs.SingleAsync(
            item => item.PublicId == importJobId);
        Assert.Equal(ImportJobStatus.Failed, jobState.Status);
        Assert.Equal(ConnectionMessages.SynchronizationStoppedByRevocation,
            jobState.FailureReason);
        Assert.False(await assertionContext.ImportedRecords.AnyAsync(
            item => item.ImportJobId == jobState.Id));
    }

    [FunctionalFact]
    public async Task GivenRevokedConnection_WhenSynchronizationIsRequested_ThenConflictIsReturned()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;
        await client.PostAsync($"/api/connections/{connection.Id}/revoke", null);

        var response = await client.PostAsJsonAsync(
            $"/api/connections/{connection.Id}/sync", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ConnectionMessages.Revoked,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAlreadyRevokedConnection_WhenRevokedAgain_ThenRequestIsIdempotent()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(client);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var first = await client.PostAsync(
            $"/api/connections/{connection.Id}/revoke", null);
        var second = await client.PostAsync(
            $"/api/connections/{connection.Id}/revoke", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(ConnectionMessages.AlreadyRevoked,
            await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAnotherOwnersConnection_WhenRevoked_ThenNotFoundMatchesMissingRecord()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var connected = await ConnectAsync(owner);
        var connection = (await connected.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        var foreign = await other.PostAsync(
            $"/api/connections/{connection.Id}/revoke", null);
        var missing = await other.PostAsync(
            $"/api/connections/{Guid.NewGuid()}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            (await missing.Content.ReadFromJsonAsync<ConnectionErrorEnvelope>())?.Errors,
            (await foreign.Content.ReadFromJsonAsync<ConnectionErrorEnvelope>())?.Errors);
        await using var context = CreateContext();
        Assert.Equal(ConnectionStatus.Active, await context.Connections
            .Where(item => item.PublicId == connection.Id)
            .Select(item => item.Status).SingleAsync());
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenRevokingConnection_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var id = Guid.NewGuid();

        var unauthorized = await anonymous.PostAsync($"/api/connections/{id}/revoke", null);
        var forbidden = await administrator.PostAsync($"/api/connections/{id}/revoke", null);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
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

    private WebApplicationFactory<Program> CreateFactory(IPluggyConnectionGateway gateway)
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
                services.RemoveAll<IPluggyConnectionGateway>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<IPluggyConnectionGateway>(gateway);
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
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

    private async Task MarkRequiresReauthenticationAsync(Guid connectionId)
    {
        await using var context = CreateContext();
        var connection = await context.Connections.SingleAsync(
            item => item.PublicId == connectionId);
        connection.MarkRequiresReauthentication(Now.AddMinutes(1));
        await context.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> ConnectAsync(
        HttpClient client,
        Guid? externalReference = null) => client.PostAsJsonAsync("/api/connections", new
        {
            DataSource = "pluggy",
            ExternalReference = externalReference ?? Guid.NewGuid()
        });

    private static StubPluggyGateway Gateway(PluggyConnectionValidationOutcome outcome) => new(
        outcome == PluggyConnectionValidationOutcome.Succeeded
            ? new(outcome, "Nubank", AccessToken)
            : new(outcome));

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
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
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_PLUGGY_CLIENT_ID"] = "client",
        ["FORTUNA_PLUGGY_CLIENT_SECRET"] = "secret",
        ["FORTUNA_PLUGGY_BASE_URL"] = "https://pluggy.example"
    };

    private sealed record ConnectionEnvelope(ConnectionData? Data);
    private sealed record SynchronizationEnvelope(SynchronizationData? Data);
    private sealed record ConnectionQueryEnvelope(ConnectionQueryData? Data);
    private sealed record RevocationEnvelope(RevocationData? Data);
    private sealed record ConnectionErrorEnvelope(IReadOnlyList<string> Errors);
    private sealed record ConnectionPage(
        IReadOnlyList<ConnectionQueryData> Data,
        int TotalItems);
    private sealed record ConnectionData(
        Guid Id,
        TransactionSourceType DataSourceType,
        string ExternalReference,
        string Institution,
        ConnectionStatus Status);
    private sealed record ConnectionQueryData(
        Guid Id,
        TransactionSourceType DataSourceType,
        string ExternalReference,
        ConnectionStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
    private sealed record RevocationData(
        Guid Id,
        ConnectionStatus Status,
        bool ImportedDataRetained,
        int StoppedSynchronizations);
    private sealed record SynchronizationData(
        Guid ImportJobId,
        ImportJobStatus Status,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd);

    private sealed class StubPluggyGateway(PluggyConnectionValidation result)
        : IPluggyConnectionGateway
    {
        public int CallCount { get; private set; }

        public Task<PluggyConnectionValidation> ValidateAsync(
            string externalReference,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class SequencedPluggyGateway(params PluggyConnectionValidation[] results)
        : IPluggyConnectionGateway
    {
        private readonly Queue<PluggyConnectionValidation> queue = new(results);

        public Task<PluggyConnectionValidation> ValidateAsync(
            string externalReference,
            CancellationToken cancellationToken) => Task.FromResult(queue.Dequeue());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
