using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class ImportJobRetryTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenFailedJobWithImportedRow_WhenRetried_ThenOnlyMissingRowIsImported()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(subject, Workbook(twoRows: true),
            retainSource: true, seedImportedRow: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);
        var body = await response.Content.ReadFromJsonAsync<RetryEnvelope>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(ImportJobStatus.Pending, body?.Data?.Status);
        Assert.Equal((0, 0, 0),
            (body!.Data!.ImportedCount, body.Data.DuplicateCount, body.Data.RejectedCount));

        await ProcessNextAsync(factory);
        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(item =>
            item.PublicId == seeded.ImportJobId);
        var background = await context.BackgroundJobs.SingleAsync(item =>
            item.Id == seeded.BackgroundJobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal((1, 1, 0),
            (job.ImportedCount, job.DuplicateCount, job.RejectedCount));
        Assert.Equal(BackgroundJobState.Succeeded, background.State);
        Assert.Equal(2, await context.FinancialTransactions.CountAsync(item =>
            item.FinancialAccount!.PublicId == seeded.AccountId));
        Assert.Equal(3, await context.ImportedRecords.CountAsync(item =>
            item.ImportJob.PublicId == seeded.ImportJobId));
    }

    [FunctionalTheory]
    [InlineData(ImportJobStatus.Pending)]
    [InlineData(ImportJobStatus.Running)]
    [InlineData(ImportJobStatus.Completed)]
    public async Task GivenJobThatDidNotFail_WhenRetried_ThenConflictIsReturned(
        ImportJobStatus status)
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedExcelAsync(subject, status, Workbook(), true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ImportJobMessages.RetryRequiresFailedJob, body!.Errors);
    }

    [FunctionalFact]
    public async Task GivenForeignOrMissingJob_WhenRetried_ThenResponsesAreIndistinguishable()
    {
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(owner, Workbook(), true, false);
        _ = await SeedExcelAsync(caller, ImportJobStatus.Failed, Workbook(), true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, caller, HeimdallRoles.User);

        var foreign = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);
        var missing = await client.PostAsync($"/api/import-jobs/{Guid.NewGuid()}/retry", null);
        var foreignBody = await foreign.Content.ReadFromJsonAsync<ErrorEnvelope>();
        var missingBody = await missing.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(foreignBody?.Errors, missingBody?.Errors);
    }

    [FunctionalFact]
    public async Task GivenRetryFailsAgain_WhenProcessed_ThenSameSafeReasonIsRecorded()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(subject, [1, 2, 3], true, false);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);
        await ProcessNextAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(item =>
            item.PublicId == seeded.ImportJobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(ExcelImportMessages.WorkbookInvalid, job.FailureReason);
        Assert.Equal(0, await context.FinancialTransactions.CountAsync(item =>
            item.FinancialAccount!.PublicId == seeded.AccountId));
    }

    [FunctionalFact]
    public async Task GivenSourceFileNotRetained_WhenRetried_ThenReuploadReasonIsReturned()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(subject, [], false, false);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ImportJobMessages.SourceFileNotRetained, body!.Errors);
        await using var context = CreateContext();
        Assert.Equal(ImportJobStatus.Failed, (await context.ImportJobs.SingleAsync(item =>
            item.PublicId == seeded.ImportJobId)).Status);
        Assert.Equal(BackgroundJobState.Failed, (await context.BackgroundJobs.SingleAsync(item =>
            item.Id == seeded.BackgroundJobId)).State);
    }

    [FunctionalFact]
    public async Task GivenSystemAdministrator_WhenRetrying_ThenFinancialJobIsForbidden()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(subject, Workbook(), true, false);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.SystemAdmin);

        var response = await client.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenConcurrentRetries_WhenSubmitted_ThenOnlyOneAttemptIsQueued()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedFailedExcelAsync(subject, Workbook(), true, false);
        await using var factory = CreateFactory();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authorize(firstClient, subject, HeimdallRoles.User);
        Authorize(secondClient, subject, HeimdallRoles.User);

        var responses = await Task.WhenAll(
            firstClient.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null),
            secondClient.PostAsync($"/api/import-jobs/{seeded.ImportJobId}/retry", null));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, factory.Services.GetRequiredService<IBackgroundJobQueue>().Depth);
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
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private async Task<SeededJob> SeedFailedExcelAsync(
        Guid subject,
        byte[] content,
        bool retainSource,
        bool seedImportedRow) => await SeedExcelAsync(
            subject,
            ImportJobStatus.Failed,
            content,
            retainSource,
            seedImportedRow);

    private async Task<SeededJob> SeedExcelAsync(
        Guid subject,
        ImportJobStatus status,
        byte[] content,
        bool retainSource,
        bool seedImportedRow = false)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var account = new FinancialAccount(
            user, $"Account {subject:N}", "Bank", FinancialAccountType.Checking,
            currency, 0, Now);
        var category = new Category(user, "Food", Now);
        var importJob = new ImportJob(user, TransactionSourceType.Excel, Now);
        var payload = JsonSerializer.Serialize(new ExcelImportJobPayload(
            importJob.PublicId,
            user.PublicId,
            account.PublicId,
            ImportTargetType.Account,
            retainSource ? content : [],
            new ExcelColumnMapping("Date", "Amount", "Direction", "Memo", "Category", "Id"),
            true));
        var backgroundJob = BackgroundJob.Create(
            ExcelImportJob.Type,
            payload,
            $"{ExcelImportJob.Type}:{importJob.PublicId:N}",
            null,
            Now);

        ImportedRecord? importedRecord = null;
        FinancialTransaction? transaction = null;
        if (seedImportedRow)
        {
            importedRecord = new ImportedRecord(
                importJob,
                "{\"Date\":\"2026-09-01\",\"Amount\":10,\"Id\":\"row-1\"}",
                ImportedRecordOutcome.Imported,
                10m,
                new DateOnly(2026, 9, 1),
                "row-1");
            transaction = new FinancialTransaction(
                user,
                account,
                category,
                TransactionDirection.Expense,
                10m,
                new DateOnly(2026, 9, 1),
                Now,
                "Earlier row");
            transaction.MarkAsImported(importedRecord, TransactionSourceType.Excel, Now);
        }

        switch (status)
        {
            case ImportJobStatus.Pending:
                break;
            case ImportJobStatus.Running:
                importJob.Start(Now.AddMinutes(1));
                backgroundJob.Start(Now.AddMinutes(1));
                break;
            case ImportJobStatus.Completed:
                importJob.Start(Now.AddMinutes(1));
                importJob.Complete(0, 0, 0, Now.AddMinutes(2));
                backgroundJob.Start(Now.AddMinutes(1));
                backgroundJob.Succeed(Now.AddMinutes(2));
                break;
            case ImportJobStatus.Failed:
                importJob.Start(Now.AddMinutes(1));
                importJob.Fail(ExcelImportMessages.WorkbookInvalid, Now.AddMinutes(2));
                backgroundJob.Start(Now.AddMinutes(1));
                backgroundJob.Fail(ExcelImportMessages.WorkbookInvalid, Now.AddMinutes(2));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        context.AddRange(user, account, category, importJob, backgroundJob);
        if (importedRecord is not null)
        {
            context.AddRange(importedRecord, transaction!);
        }

        await context.SaveChangesAsync();
        return new SeededJob(importJob.PublicId, backgroundJob.Id, account.PublicId);
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

    private static async Task ProcessNextAsync(WebApplicationFactory<Program> factory)
    {
        var queue = factory.Services.GetRequiredService<IBackgroundJobQueue>();
        var id = await queue.DequeueAsync(CancellationToken.None);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BackgroundJobProcessor>()
            .ProcessAsync(id, CancellationToken.None);
    }

    private static byte[] Workbook(bool twoRows = false)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Transactions");
        sheet.Cell("A1").Value = "Date";
        sheet.Cell("B1").Value = "Amount";
        sheet.Cell("C1").Value = "Direction";
        sheet.Cell("D1").Value = "Memo";
        sheet.Cell("E1").Value = "Category";
        sheet.Cell("F1").Value = "Id";
        Row(sheet, 2, new DateTime(2026, 9, 1), 10m, "Earlier row", "row-1");
        if (twoRows)
        {
            Row(sheet, 3, new DateTime(2026, 9, 2), 20m, "New row", "row-2");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Row(
        IXLWorksheet sheet,
        int row,
        DateTime date,
        decimal amount,
        string memo,
        string id)
    {
        sheet.Cell(row, 1).Value = date;
        sheet.Cell(row, 2).Value = amount;
        sheet.Cell(row, 3).Value = "expense";
        sheet.Cell(row, 4).Value = memo;
        sheet.Cell(row, 5).Value = "Food";
        sheet.Cell(row, 6).Value = id;
    }

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
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record SeededJob(
        Guid ImportJobId,
        Guid BackgroundJobId,
        Guid AccountId);
    private sealed record RetryEnvelope(RetryData? Data);
    private sealed record RetryData(
        Guid Id,
        ImportJobStatus Status,
        int ImportedCount,
        int DuplicateCount,
        int RejectedCount);
    private sealed record ErrorEnvelope(IReadOnlyList<string> Errors);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
