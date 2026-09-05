using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
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

public sealed class TransactionReconciliationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedRecordWithinTolerance_WhenReconciled_ThenLinkIsStoredWithoutDiscrepancy()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAsync(subject, 10m, Today.AddDays(-4), 10.02m, Today.AddDays(-2));

        var response = await ReconcileAsync(client, seed);
        var envelope = await response.Content.ReadFromJsonAsync<ReconciliationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope?.Data?.IsReconciled);
        Assert.False(envelope?.Data?.Reconciliation?.HasDiscrepancy);
        Assert.Equal(seed.JobId, envelope?.Data?.Reconciliation?.ImportJobId);
        Assert.Equal(seed.RecordId, envelope?.Data?.Reconciliation?.ImportedRecordId);
        Assert.Contains(TransactionMessages.ReconciledSuccessfully, envelope!.Messages);

        var retrieved = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{seed.TransactionId}");
        Assert.True(retrieved?.Data?.IsReconciled);
        Assert.Equal(seed.JobId, retrieved?.Data?.ImportJobId);
        Assert.Equal(seed.RecordId, retrieved?.Data?.ImportedRecordId);

        await using var context = CreateContext();
        var stored = await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == seed.TransactionId);
        var raw = await context.ImportedRecords.SingleAsync(item => item.Id == seed.RecordId);
        Assert.Equal(seed.RecordId, stored.ImportedRecordId);
        using var expectedPayload = JsonDocument.Parse(seed.RawPayload);
        using var storedPayload = JsonDocument.Parse(raw.RawPayload);
        Assert.True(JsonElement.DeepEquals(
            expectedPayload.RootElement,
            storedPayload.RootElement));
    }

    [FunctionalFact]
    public async Task GivenValuesBeyondTolerance_WhenReconciled_ThenDiscrepancyReportsBothFigures()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var transactionDate = Today.AddDays(-10);
        var importedDate = Today.AddDays(-5);
        var seed = await SeedAsync(subject, 10m, transactionDate, 12m, importedDate);

        var response = await ReconcileAsync(client, seed);
        var envelope = await response.Content.ReadFromJsonAsync<ReconciliationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope?.Data?.Reconciliation?.HasDiscrepancy);
        Assert.Equal(10m, envelope?.Data?.Reconciliation?.TransactionAmount);
        Assert.Equal(12m, envelope?.Data?.Reconciliation?.ImportedAmount);
        Assert.Equal(transactionDate, envelope?.Data?.Reconciliation?.TransactionOccurredOn);
        Assert.Equal(importedDate, envelope?.Data?.Reconciliation?.ImportedOccurredOn);
    }

    [FunctionalFact]
    public async Task GivenMatchedRecord_WhenUnreconciled_ThenAnotherTransactionCanUseIt()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAsync(subject, 10m, Today.AddDays(-2), 10m, Today.AddDays(-2));
        var otherId = await SeedTransactionAsync(subject, 11m);
        Assert.Equal(HttpStatusCode.OK, (await ReconcileAsync(client, seed)).StatusCode);

        var conflict = await client.PostAsJsonAsync(
            $"/api/transactions/{otherId}/reconcile",
            new { ImportJobId = seed.JobId, ImportedRecordId = seed.RecordId });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictText = await conflict.Content.ReadAsStringAsync();
        Assert.Contains(TransactionMessages.ImportedRecordAlreadyMatched, conflictText,
            StringComparison.Ordinal);
        Assert.Contains(seed.TransactionId.ToString(), conflictText, StringComparison.Ordinal);

        var unreconciled = await client.PostAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}/reconcile",
            new { Unreconcile = true });
        var reused = await client.PostAsJsonAsync(
            $"/api/transactions/{otherId}/reconcile",
            new { ImportJobId = seed.JobId, ImportedRecordId = seed.RecordId });
        var already = await client.PostAsJsonAsync(
            $"/api/transactions/{otherId}/reconcile",
            new { ImportJobId = seed.JobId, ImportedRecordId = seed.RecordId });

        Assert.Equal(HttpStatusCode.OK, unreconciled.StatusCode);
        Assert.Contains(TransactionMessages.UnreconciledSuccessfully,
            await unreconciled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, already.StatusCode);
        Assert.Contains(TransactionMessages.AlreadyReconciled,
            await already.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenForeignTransactionOrRecord_WhenReconciled_ThenNotFoundIsReturned()
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
        var ownerSeed = await SeedAsync(
            ownerSubject, 10m, Today.AddDays(-2), 10m, Today.AddDays(-2));
        var otherSeed = await SeedAsync(
            otherSubject, 20m, Today.AddDays(-2), 20m, Today.AddDays(-2));

        var foreignTransaction = await other.PostAsJsonAsync(
            $"/api/transactions/{ownerSeed.TransactionId}/reconcile",
            new { ImportJobId = otherSeed.JobId, ImportedRecordId = otherSeed.RecordId });
        var foreignRecord = await owner.PostAsJsonAsync(
            $"/api/transactions/{ownerSeed.TransactionId}/reconcile",
            new { ImportJobId = otherSeed.JobId, ImportedRecordId = otherSeed.RecordId });

        Assert.Equal(HttpStatusCode.NotFound, foreignTransaction.StatusCode);
        Assert.Contains(TransactionMessages.NotFound,
            await foreignTransaction.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreignRecord.StatusCode);
        Assert.Contains(TransactionMessages.ImportedRecordNotFound,
            await foreignRecord.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenInvalidBodyOrUnmatchedTransaction_WhenUnreconciled_ThenRequestIsRejected()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        await EnsureProfileAsync(client);
        var seed = await SeedAsync(subject, 10m, Today.AddDays(-2), 10m, Today.AddDays(-2));

        var invalid = await client.PostAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}/reconcile",
            new { ImportJobId = seed.JobId, ImportedRecordId = 0 });
        var notReconciled = await client.PostAsJsonAsync(
            $"/api/transactions/{seed.TransactionId}/reconcile",
            new { Unreconcile = true });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(TransactionMessages.ImportedRecordIdRequired,
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, notReconciled.StatusCode);
        Assert.Contains(TransactionMessages.NotReconciled,
            await notReconciled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenReconciled_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var body = new { ImportJobId = Guid.NewGuid(), ImportedRecordId = 1 };

        var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}/reconcile", body);
        var administratorResponse = await administrator.PostAsJsonAsync(
            $"/api/transactions/{Guid.NewGuid()}/reconcile", body);

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

    private async Task<ReconciliationSeed> SeedAsync(
        Guid subject,
        decimal transactionAmount,
        DateOnly transactionDate,
        decimal importedAmount,
        DateOnly importedDate)
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
            0m,
            DateTimeOffset.UtcNow);
        var category = new Category(user, $"Category {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            transactionAmount,
            transactionDate,
            DateTimeOffset.UtcNow);
        var job = new ImportJob(user, TransactionSourceType.Excel, DateTimeOffset.UtcNow);
        var rawPayload = JsonSerializer.Serialize(new
        {
            Amount = importedAmount,
            OccurredOn = importedDate
        });
        var record = new ImportedRecord(
            job,
            rawPayload,
            ImportedRecordOutcome.Imported,
            importedAmount,
            importedDate);
        context.AddRange(account, category, transaction, job, record);
        await context.SaveChangesAsync();
        return new ReconciliationSeed(
            transaction.PublicId,
            job.PublicId,
            record.Id,
            rawPayload);
    }

    private async Task<Guid> SeedTransactionAsync(Guid subject, decimal amount)
    {
        await using var context = CreateContext();
        var user = await UserAsync(context, subject);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var account = new FinancialAccount(
            user,
            $"Other {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            0m,
            DateTimeOffset.UtcNow);
        var category = new Category(user, $"Other {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            user,
            account,
            category,
            TransactionDirection.Expense,
            amount,
            Today.AddDays(-2),
            DateTimeOffset.UtcNow);
        context.AddRange(account, category, transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static Task<Domain.Users.UserProfile> UserAsync(
        AppDbContext context,
        Guid subject) => context.UserProfiles.SingleAsync(item =>
        item.ExternalSubject == subject.ToString("D"));

    private static Task<HttpResponseMessage> ReconcileAsync(
        HttpClient client,
        ReconciliationSeed seed) => client.PostAsJsonAsync(
        $"/api/transactions/{seed.TransactionId}/reconcile",
        new { ImportJobId = seed.JobId, ImportedRecordId = seed.RecordId });

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
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE"] = "0.02",
        ["FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS"] = "2"
    };

    private sealed record ReconciliationSeed(
        Guid TransactionId,
        Guid JobId,
        long RecordId,
        string RawPayload);
    private sealed record ReconciliationEnvelope(
        ReconciliationData? Data,
        IReadOnlyCollection<string> Messages);
    private sealed record ReconciliationData(
        Guid Id,
        bool IsReconciled,
        ReconciliationDetail? Reconciliation);
    private sealed record ReconciliationDetail(
        Guid ImportJobId,
        long ImportedRecordId,
        bool HasDiscrepancy,
        decimal TransactionAmount,
        decimal ImportedAmount,
        DateOnly TransactionOccurredOn,
        DateOnly ImportedOccurredOn);
    private sealed record TransactionEnvelope(TransactionData? Data);
    private sealed record TransactionData(
        bool IsReconciled,
        Guid? ImportJobId,
        long? ImportedRecordId);
}
