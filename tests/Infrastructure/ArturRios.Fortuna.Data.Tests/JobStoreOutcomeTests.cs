using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Exports;
using ArturRios.Fortuna.Data.Ingestion;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

/// <summary>Job-backed stores report a vanished or finished work item as an outcome, never a crash.</summary>
public sealed class JobStoreOutcomeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T12:00:00Z");

    private readonly string path =
        Path.Combine(Path.GetTempPath(), $"fortuna-job-outcomes-{Guid.NewGuid():N}.db");

    private AppDbContext context = null!;

    public async Task InitializeAsync()
    {
        context = CreateContext(path);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    [FunctionalFact]
    public async Task GivenUnknownImportJobs_WhenCompletedOrFailed_ThenNotFoundIsReturned()
    {
        var excel = new EfExcelImportStore(context);
        var pdf = new EfPdfInvoiceImportStore(context);
        var pluggy = new EfPluggySynchronizationStore(context);
        var unknown = Guid.NewGuid();

        var excelCompletion = await excel.CompleteAsync(
            unknown, Guid.NewGuid(), Guid.NewGuid(), ImportTargetType.Account, false, [], Now,
            CancellationToken.None);
        var pdfCompletion = await pdf.CompleteAsync(
            unknown, Guid.NewGuid(), Guid.NewGuid(), Invoice(new DateOnly(2026, 9, 20)), Now,
            CancellationToken.None);
        var pluggyCompletion = await pluggy.CompleteAsync(
            unknown, new PluggySynchronizationBatch([], []), Now, CancellationToken.None);

        Assert.Equal(ImportCompletionOutcome.JobNotFound, excelCompletion.Outcome);
        Assert.Equal(ImportCompletionOutcome.JobNotFound, pdfCompletion.Outcome);
        Assert.Equal(ImportCompletionOutcome.JobNotFound, pluggyCompletion.Outcome);
        Assert.Equal(JobTransitionOutcome.NotFound,
            await excel.FailAsync(unknown, "reason", Now, CancellationToken.None));
        Assert.Equal(JobTransitionOutcome.NotFound,
            await pdf.FailAsync(unknown, "reason", Now, CancellationToken.None));
        Assert.Equal(JobTransitionOutcome.NotFound,
            await pluggy.FailAsync(unknown, "reason", true, Now, CancellationToken.None));
    }

    [FunctionalFact]
    public async Task GivenDeletedExcelTarget_WhenCompleted_ThenTargetUnavailableIsReturned()
    {
        var (user, currency) = await SeedUserAsync();
        var account = new FinancialAccount(
            user, "Checking", null, FinancialAccountType.Checking, currency, 0m, Now);
        var job = new ImportJob(user, TransactionSourceType.Excel, Now);
        job.Start(Now);
        context.AddRange(account, job);
        await context.SaveChangesAsync();

        var result = await new EfExcelImportStore(context).CompleteAsync(
            job.PublicId, user.PublicId, Guid.NewGuid(), ImportTargetType.Account, false, [], Now,
            CancellationToken.None);

        Assert.Equal(ImportCompletionOutcome.TargetUnavailable, result.Outcome);
    }

    [FunctionalFact]
    public async Task GivenFinishedImportJob_WhenFailedAgain_ThenNotRunningIsReturned()
    {
        var (user, _) = await SeedUserAsync();
        var job = new ImportJob(user, TransactionSourceType.Excel, Now);
        job.Start(Now);
        job.Complete(0, 0, 0, Now);
        context.Add(job);
        await context.SaveChangesAsync();

        var outcome = await new EfExcelImportStore(context).FailAsync(
            job.PublicId, "late failure", Now, CancellationToken.None);

        Assert.Equal(JobTransitionOutcome.NotRunning, outcome);
    }

    [FunctionalFact]
    public async Task GivenInvoiceDueBeforePeriodEnd_WhenCompleted_ThenSpecificRejectionIsReturned()
    {
        var (user, card, job) = await SeedPdfJobAsync();

        var result = await new EfPdfInvoiceImportStore(context).CompleteAsync(
            job.PublicId, user.PublicId, card.PublicId, Invoice(new DateOnly(2026, 8, 12)), Now,
            CancellationToken.None);

        Assert.Equal(ImportCompletionOutcome.Rejected, result.Outcome);
        Assert.Equal(PdfInvoiceImportMessages.BillingCycleInvalid, result.Reason);
        Assert.Empty(await context.CreditCardStatements.ToListAsync());
    }

    [FunctionalFact]
    public async Task GivenNonReconcilingSummary_WhenCompleted_ThenSpecificRejectionIsReturnedAndNothingIsSaved()
    {
        var (user, card, job) = await SeedPdfJobAsync();
        var invoice = Invoice(new DateOnly(2026, 9, 20)) with { AmountDue = 999m };

        var result = await new EfPdfInvoiceImportStore(context).CompleteAsync(
            job.PublicId, user.PublicId, card.PublicId, invoice, Now, CancellationToken.None);

        Assert.Equal(ImportCompletionOutcome.Rejected, result.Outcome);
        Assert.Equal(PdfInvoiceImportMessages.SummaryDoesNotReconcile, result.Reason);
        Assert.Empty(await context.CreditCardStatements.AsNoTracking().ToListAsync());
        Assert.Empty(await context.FinancialTransactions.AsNoTracking().ToListAsync());
    }

    [FunctionalFact]
    public async Task GivenUnknownExport_WhenCompletedOrFailed_ThenNotFoundIsReturned()
    {
        var store = new EfDataExportStore(context);
        var unknown = Guid.NewGuid();

        Assert.Equal(JobTransitionOutcome.NotFound, await store.CompleteAsync(
            unknown, 1, "text/csv", "exports/file.csv", Now, CancellationToken.None));
        Assert.Equal(JobTransitionOutcome.NotFound,
            await store.FailAsync(unknown, "reason", Now, CancellationToken.None));
        Assert.Null(await store.StartAsync(unknown, Now, CancellationToken.None));
    }

    [FunctionalFact]
    public async Task GivenInterruptedRunningExport_WhenStartedAgain_ThenItResumesAndFinishedOnesDoNot()
    {
        var (user, _) = await SeedUserAsync();
        var running = Export(user);
        running.Start(Now);
        var finished = Export(user);
        finished.Start(Now);
        finished.Fail("earlier failure", Now);
        context.AddRange(running, finished);
        await context.SaveChangesAsync();
        var store = new EfDataExportStore(context);

        var resumed = await store.StartAsync(running.PublicId, Now, CancellationToken.None);
        var skipped = await store.StartAsync(finished.PublicId, Now, CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Null(skipped);
        Assert.Equal(JobTransitionOutcome.NotRunning,
            await store.CompleteAsync(finished.PublicId, 1, "text/csv", "exports/x.csv", Now, CancellationToken.None));
    }

    private async Task<(UserProfile User, Currency Currency)> SeedUserAsync()
    {
        var currency = new Currency("BRL", "Brazilian Real", 2);
        var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
        context.AddRange(currency, user);
        await context.SaveChangesAsync();

        return (user, currency);
    }

    private async Task<(UserProfile User, CreditCard Card, ImportJob Job)> SeedPdfJobAsync()
    {
        var (user, currency) = await SeedUserAsync();
        var card = new CreditCard(user, "Nubank", "Nubank", currency, 1000m, 12, 20, null, Now);
        var job = new ImportJob(user, TransactionSourceType.Pdf, Now);
        job.Start(Now);
        context.AddRange(card, job);
        await context.SaveChangesAsync();

        return (user, card, job);
    }

    private DataExport Export(UserProfile user)
    {
        var export = new DataExport(
            user,
            DataExportFormat.Csv,
            "en-US",
            "export.csv",
            "{}",
            Now,
            Now.AddHours(24));
        var backgroundJob = BackgroundJob.Create(
            DataExportJob.Type,
            "{}",
            $"{DataExportJob.Type}:{export.PublicId:N}",
            null,
            Now);
        export.AttachBackgroundJob(backgroundJob);
        context.Add(backgroundJob);

        return export;
    }

    private static ParsedPdfInvoice Invoice(DateOnly dueDate) => new(
        "Nubank credit card invoice",
        dueDate,
        new DateOnly(2026, 8, 13),
        new DateOnly(2026, 7, 13),
        new DateOnly(2026, 8, 12),
        0m,
        0m,
        10m,
        0m,
        0m,
        10m,
        10m,
        0m,
        [new(1, "{}", new DateOnly(2026, 8, 1), null, "Purchase", 10m, PdfInvoiceLineKind.Purchase)]);

    private static AppDbContext CreateContext(string databasePath)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, databasePath);

        return new AppDbContext(builder.Options, NullLoggerFactory.Instance, DatabaseDiagnosticsOptions.Disabled);
    }
}
