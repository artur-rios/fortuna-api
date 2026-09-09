using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfImportJobRetryStore(AppDbContext context) : IImportJobRetryStore
{
    public async Task<RetryImportJobResult> RetryAsync(
        Guid userId,
        Guid importJobId,
        DateTimeOffset retriedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var ownerId = await context.UserProfiles
            .AsNoTracking()
            .Where(user => user.PublicId == userId)
            .Select(user => (long?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownerId is null)
        {
            return Result(RetryImportJobOutcome.NotFound);
        }

        var importJob = context.Database.IsSqlite()
            ? await context.ImportJobs.SingleOrDefaultAsync(
                item => item.PublicId == importJobId && item.UserId == ownerId.Value,
                cancellationToken)
            : await context.ImportJobs
                .FromSqlInterpolated(
                    $"SELECT * FROM fortuna.import_job WHERE public_id = {importJobId} AND user_id = {ownerId.Value} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        if (importJob is null)
        {
            return Result(RetryImportJobOutcome.NotFound);
        }

        if (importJob.Status != ImportJobStatus.Failed)
        {
            return Result(RetryImportJobOutcome.NotFailed, importJob);
        }

        var type = JobType(importJob.SourceType);
        var idempotencyKey = $"{type}:{importJob.PublicId:N}";
        var backgroundJob = await context.BackgroundJobs.SingleOrDefaultAsync(
            job => job.IdempotencyKey == idempotencyKey && job.Type == type,
            cancellationToken);
        if (backgroundJob is null || backgroundJob.State != BackgroundJobState.Failed)
        {
            return Result(RetryImportJobOutcome.NotFailed, importJob);
        }

        if (!HasRetainedSource(importJob, backgroundJob))
        {
            return Result(RetryImportJobOutcome.SourceFileNotRetained, importJob);
        }

        importJob.Retry(retriedAt);
        backgroundJob.Retry();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(RetryImportJobOutcome.Succeeded, importJob, backgroundJob.Id);
    }

    private static bool HasRetainedSource(ImportJob importJob, BackgroundJob backgroundJob)
    {
        try
        {
            return importJob.SourceType switch
            {
                TransactionSourceType.Excel =>
                    JsonSerializer.Deserialize<ExcelImportJobPayload>(backgroundJob.Payload)
                        is { Content.Length: > 0 } excel &&
                    excel.ImportJobId == importJob.PublicId,
                TransactionSourceType.Pdf =>
                    JsonSerializer.Deserialize<PdfInvoiceImportJobPayload>(backgroundJob.Payload)
                        is { Content.Length: > 0 } pdf &&
                    pdf.ImportJobId == importJob.PublicId,
                TransactionSourceType.Pluggy =>
                    JsonSerializer.Deserialize<PluggySynchronizationJobPayload>(backgroundJob.Payload)
                        is { } pluggy && pluggy.ImportJobId == importJob.PublicId,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string JobType(TransactionSourceType sourceType) => sourceType switch
    {
        TransactionSourceType.Excel => ExcelImportJob.Type,
        TransactionSourceType.Pdf => PdfInvoiceImportJob.Type,
        TransactionSourceType.Pluggy => PluggySynchronizationJob.Type,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
    };

    private static RetryImportJobResult Result(
        RetryImportJobOutcome outcome,
        ImportJob? job = null,
        Guid? backgroundJobId = null) => new(
            outcome,
            job is null ? null : new RetryImportJobSnapshot(
                job.PublicId,
                job.SourceType,
                job.Status,
                job.PeriodStart,
                job.PeriodEnd,
                job.ImportedCount,
                job.DuplicateCount,
                job.RejectedCount,
                job.CreatedAt,
                job.UpdatedAt),
            backgroundJobId);
}
