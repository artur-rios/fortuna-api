using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
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

public sealed class ExcelImportTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 14, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenMappedWorkbook_WhenProcessedTwice_ThenRowsAndDuplicatesAreReported()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 1), 25.50m, "expense", "Lunch", "Food", "row-1");
            Row(sheet, 3, "not-a-date", 5m, "expense", "Bad", "Food", "row-2");
            Row(sheet, 4, new DateTime(2026, 9, 1), 25.50m, "expense", "Lunch", "Food", "row-1");
        });

        var first = await ImportAsync(client, accountId, workbook, createCategories: true);
        var firstJobId = (await first.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);
        var second = await ImportAsync(client, accountId, workbook, createCategories: true);
        var secondJobId = (await second.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        await using var context = CreateContext();
        var firstJob = await context.ImportJobs.SingleAsync(item => item.PublicId == firstJobId);
        var secondJob = await context.ImportJobs.SingleAsync(item => item.PublicId == secondJobId);
        Assert.Equal((1, 1, 1),
            (firstJob.ImportedCount, firstJob.DuplicateCount, firstJob.RejectedCount));
        Assert.Equal((0, 2, 1),
            (secondJob.ImportedCount, secondJob.DuplicateCount, secondJob.RejectedCount));
        Assert.Equal(1, await context.FinancialTransactions.CountAsync(item =>
            item.FinancialAccount!.PublicId == accountId && !item.IsDeleted));
        Assert.True(await context.Categories.AnyAsync(item =>
            item.User.ExternalSubject == subject.ToString("D") && item.Name == "Food"));
        var records = await context.ImportedRecords
            .Where(item => item.ImportJob.PublicId == firstJobId)
            .OrderBy(item => item.Id)
            .ToArrayAsync();
        Assert.Equal(3, records.Length);
        Assert.Equal(ImportedRecordOutcome.Imported, records[0].Outcome);
        Assert.Equal(ImportedRecordOutcome.Rejected, records[1].Outcome);
        Assert.Equal(ExcelImportMessages.RowDateInvalid, records[1].RejectionReason);
        using var raw = JsonDocument.Parse(records[0].RawPayload);
        Assert.Equal("Lunch", raw.RootElement.GetProperty("Memo").GetString());
    }

    [FunctionalFact]
    public async Task GivenCategoryCreationDisabled_WhenProcessed_ThenDefaultCategoryIsUsed()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 2), 10m, "expense", "Book", "Novel", "book-1");
        });

        var response = await ImportAsync(client, accountId, workbook, createCategories: false);
        await ProcessNextAsync(factory);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var context = CreateContext();
        var transaction = await context.FinancialTransactions
            .Include(item => item.Category)
            .SingleAsync(item => item.FinancialAccount!.PublicId == accountId);
        Assert.Equal("Uncategorized", transaction.Category.Name);
        Assert.False(await context.Categories.AnyAsync(item =>
            item.User.ExternalSubject == subject.ToString("D") && item.Name == "Novel"));
    }

    [FunctionalFact]
    public async Task GivenMissingRequiredColumn_WhenImported_ThenNothingIsQueued()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            sheet.Cell("A1").Value = "Date";
            sheet.Cell("B1").Value = "Direction";
        });

        var response = await ImportAsync(client, accountId, workbook);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ExcelImportMessages.ColumnNotFound("Amount"),
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.ImportJobs.AnyAsync(item =>
            item.User.ExternalSubject == subject.ToString("D")));
    }

    [FunctionalFact]
    public async Task GivenUnreadableOrOversizedFile_WhenImported_ThenNothingIsQueued()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory(maximumBytes: 10);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var unreadable = await ImportAsync(client, accountId, [1, 2, 3]);
        var oversized = await ImportAsync(client, accountId, new byte[11]);

        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);
        Assert.Contains(ExcelImportMessages.WorkbookInvalid,
            await unreadable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains(ExcelImportMessages.FileTooLarge,
            await oversized.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenBodyBeyondConfiguredLimit_WhenImported_ThenDataOutputErrorIsReturned()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory(maximumBytes: 10);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await ImportAsync(client, accountId, new byte[256 * 1024]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ExcelImportMessages.FileTooLarge, body, StringComparison.Ordinal);
        Assert.Contains("\"errors\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [FunctionalFact]
    public async Task GivenForeignOrMissingTarget_WhenImported_ThenResponsesAreIndistinguishable()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(ownerSubject);
        await SeedAccountAsync(otherSubject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, otherSubject, HeimdallRoles.User);
        var workbook = ValidWorkbook();

        var foreign = await ImportAsync(client, accountId, workbook);
        var missing = await ImportAsync(client, Guid.NewGuid(), workbook);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            (await missing.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors,
            (await foreign.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors);
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenImporting_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var unauthorized = await ImportAsync(anonymous, Guid.NewGuid(), ValidWorkbook());
        var forbidden = await ImportAsync(administrator, Guid.NewGuid(), ValidWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenRowInSettledCycle_WhenProcessed_ThenItArrivesLateOnTheNextOpenStatement()
    {
        var subject = Guid.NewGuid();
        var (cardId, settledId) = await SeedCardWithSettledCycleAsync(subject, new DateOnly(2026, 9, 1));
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 1), 30m, "expense", "Late", "Food", "late-1");
            Row(sheet, 3, new DateTime(2026, 9, 20), 20m, "expense", "Current", "Food", "current-1");
        });

        var response = await ImportAsync(
            client, cardId, workbook, targetType: ImportTargetType.CreditCard);
        var jobId = (await response.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(item => item.PublicId == jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal((2, 0, 0), (job.ImportedCount, job.DuplicateCount, job.RejectedCount));
        Assert.Equal(BackgroundJobState.Succeeded, (await context.BackgroundJobs.SingleAsync(
            item => item.IdempotencyKey.EndsWith(jobId.ToString("N")))).State);
        var settled = await context.CreditCardStatements.SingleAsync(item => item.Id == settledId);
        Assert.Equal(CreditCardStatementStatus.Settled, settled.Status);
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            item.StatementId == settledId && item.SourceType == TransactionSourceType.Excel));
        var imported = await context.FinancialTransactions
            .Include(item => item.Statement)
            .Where(item => item.CreditCard!.PublicId == cardId &&
                item.SourceType == TransactionSourceType.Excel)
            .ToArrayAsync();
        Assert.Equal(2, imported.Length);
        Assert.All(imported, item =>
        {
            Assert.Equal(CreditCardStatementStatus.Open, item.Statement!.Status);
            Assert.True(item.Statement.PeriodStart > settled.PeriodEnd);
        });
        Assert.True(imported.Single(item => item.Description == "Late").IsLateArriving);
        Assert.False(imported.Single(item => item.Description == "Current").IsLateArriving);
        Assert.Equal(50m, imported[0].Statement!.PurchaseTotal);
    }

    [FunctionalFact]
    public async Task GivenRowThatCannotBeStored_WhenProcessed_ThenNoRowIsCommittedAndBothJobsFail()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 1), 10m, "expense", "Fits", "Food", "fits-1");
            // Beyond numeric(19,4): the database refuses it when the import is saved.
            Row(sheet, 3, new DateTime(2026, 9, 2), 1_000_000_000_000_000m, "expense", "Overflow",
                "Food", "overflow-1");
        });

        var response = await ImportAsync(client, accountId, workbook, createCategories: true);
        var jobId = (await response.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(item => item.PublicId == jobId);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(ImportJobMessages.ProcessingFailed, job.FailureReason);
        Assert.Equal(BackgroundJobState.Failed, (await context.BackgroundJobs.SingleAsync(
            item => item.IdempotencyKey.EndsWith(jobId.ToString("N")))).State);
        Assert.False(await context.FinancialTransactions.AnyAsync(item =>
            item.FinancialAccount!.PublicId == accountId));
        Assert.False(await context.ImportedRecords.AnyAsync(item => item.ImportJob.PublicId == jobId));
        Assert.False(await context.Categories.AnyAsync(item =>
            item.User.ExternalSubject == subject.ToString("D") && item.Name == "Food"));
    }

    [FunctionalFact]
    public async Task GivenRowsThatCannotBeStored_WhenProcessed_ThenOnlyThoseRowsAreRejected()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 1), 10m, "expense", "Fits", "Food", "fits-1");
            Row(sheet, 3, new DateTime(2026, 9, 2), 12m, "expense", "Long", new string('c', 201),
                "long-1");
            // Positive, but zero once stored at the column's four decimal places.
            Row(sheet, 4, new DateTime(2026, 9, 3), 0.00004m, "expense", "Dust", "Food", "dust-1");
        });

        var response = await ImportAsync(client, accountId, workbook, createCategories: true);
        var jobId = (await response.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(item => item.PublicId == jobId);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal((1, 0, 2), (job.ImportedCount, job.DuplicateCount, job.RejectedCount));
        var rejected = await context.ImportedRecords
            .Where(item =>
                item.ImportJob.PublicId == jobId && item.Outcome == ImportedRecordOutcome.Rejected)
            .OrderBy(item => item.Id)
            .Select(item => item.RejectionReason)
            .ToArrayAsync();
        Assert.Equal(
            new string?[] { ExcelImportMessages.RowCategoryTooLong, ExcelImportMessages.RowAmountInvalid },
            rejected.AsEnumerable());
    }

    [FunctionalFact]
    public async Task GivenAmountFinerThanStored_WhenImportedTwice_ThenSecondImportIsDuplicate()
    {
        var subject = Guid.NewGuid();
        var accountId = await SeedAccountAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var workbook = Workbook(sheet =>
        {
            Headers(sheet);
            Row(sheet, 2, new DateTime(2026, 9, 1), 100m / 3m, "expense", "Third", "Food", "");
        });

        await ImportAsync(client, accountId, workbook);
        await ProcessNextAsync(factory);
        var second = await ImportAsync(client, accountId, workbook);
        var secondJobId = (await second.Content.ReadFromJsonAsync<ImportEnvelope>())!.Data!.ImportJobId;
        await ProcessNextAsync(factory);

        await using var context = CreateContext();
        var secondJob = await context.ImportJobs.SingleAsync(item => item.PublicId == secondJobId);
        Assert.Equal((0, 1, 0),
            (secondJob.ImportedCount, secondJob.DuplicateCount, secondJob.RejectedCount));
        Assert.Equal(33.3333m, (await context.FinancialTransactions.SingleAsync(item =>
            item.FinancialAccount!.PublicId == accountId)).Amount);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(int maximumBytes = 10 * 1024 * 1024)
    {
        foreach (var setting in ValidSettings(maximumBytes))
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

    private async Task<Guid> SeedAccountAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var account = new FinancialAccount(
            user, $"Account {subject:N}", "Bank", FinancialAccountType.Checking,
            currency, 0, Now);
        context.AddRange(user, account);
        await context.SaveChangesAsync();

        return account.PublicId;
    }

    private async Task<(Guid CardId, long SettledStatementId)> SeedCardWithSettledCycleAsync(
        Guid subject,
        DateOnly settledDate)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var card = new CreditCard(
            user, $"Card {subject:N}", "Bank", currency, 5000m, 12, 10, "1234", Now);
        var category = new Category(user, "General", Now);
        var statement = new CreditCardStatement(
            card, BillingCycle.Containing(settledDate, card.ClosingDay, card.DueDay), Now);
        statement.Close(Now);
        var settlement = new FinancialTransaction(
            user, card, category, TransactionDirection.Earning, 1m,
            statement.DueDate, Now, "Statement payment");
        statement.Settle(settlement, Now);
        context.AddRange(user, card, category, statement, settlement);
        await context.SaveChangesAsync();

        return (card.PublicId, statement.Id);
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

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client,
        Guid targetId,
        byte[] workbook,
        bool createCategories = false,
        ImportTargetType targetType = ImportTargetType.Account)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(targetId.ToString("D")), "TargetId");
        form.Add(new StringContent(targetType.ToString()), "TargetType");
        form.Add(new StringContent("Date"), "DateColumn");
        form.Add(new StringContent("Amount"), "AmountColumn");
        form.Add(new StringContent("Direction"), "DirectionColumn");
        form.Add(new StringContent("Memo"), "DescriptionColumn");
        form.Add(new StringContent("Category"), "CategoryColumn");
        form.Add(new StringContent("Id"), "ExternalIdColumn");
        form.Add(new StringContent(createCategories.ToString()), "CreateMissingCategories");
        form.Add(new ByteArrayContent(workbook), "File", "transactions.xlsx");

        return await client.PostAsync("/api/imports/excel", form);
    }

    private static byte[] ValidWorkbook() => Workbook(sheet =>
    {
        Headers(sheet);
        Row(sheet, 2, new DateTime(2026, 9, 1), 10m, "expense", "Lunch", "Food", "1");
    });

    private static byte[] Workbook(Action<IXLWorksheet> populate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Transactions");
        populate(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static void Headers(IXLWorksheet sheet)
    {
        sheet.Cell("A1").Value = "Date";
        sheet.Cell("B1").Value = "Amount";
        sheet.Cell("C1").Value = "Direction";
        sheet.Cell("D1").Value = "Memo";
        sheet.Cell("E1").Value = "Category";
        sheet.Cell("F1").Value = "Id";
    }

    private static void Row(
        IXLWorksheet sheet,
        int row,
        object date,
        decimal amount,
        string direction,
        string memo,
        string category,
        string id)
    {
        if (date is DateTime typed)
        {
            sheet.Cell(row, 1).Value = typed;
        }
        else
        {
            sheet.Cell(row, 1).Value = date.ToString();
        }

        sheet.Cell(row, 2).Value = amount;
        sheet.Cell(row, 3).Value = direction;
        sheet.Cell(row, 4).Value = memo;
        sheet.Cell(row, 5).Value = category;
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

    private static Dictionary<string, string?> ValidSettings(int maximumBytes) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_EXCEL_IMPORT_MAX_BYTES"] = maximumBytes.ToString(),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record ImportEnvelope(ImportData? Data);
    private sealed record ImportData(Guid ImportJobId, ImportJobStatus Status);
    private sealed record ErrorEnvelope(IReadOnlyList<string> Errors);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
