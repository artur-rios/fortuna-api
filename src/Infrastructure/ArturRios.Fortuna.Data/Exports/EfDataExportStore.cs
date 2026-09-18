using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Exports;

public sealed class EfDataExportStore(AppDbContext context) :
    IDataExportStore,
    IDataExportReader,
    IPersonalDataExportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QueueDataExportResult> QueueAsync(
        QueueDataExportRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == request.UserId,
            cancellationToken);
        var requestJson = JsonSerializer.Serialize(request.Specification, JsonOptions);
        var export = new DataExport(
            user,
            request.Specification.Format,
            request.Specification.Locale,
            request.FileName,
            requestJson,
            request.RequestedAt,
            request.ExpiresAt);
        var backgroundJob = BackgroundJob.Create(
            DataExportJob.Type,
            JsonSerializer.Serialize(new DataExportJobPayload(export.PublicId), JsonOptions),
            $"{DataExportJob.Type}:{export.PublicId:N}",
            request.CorrelationId,
            request.RequestedAt);
        export.AttachBackgroundJob(backgroundJob);
        context.AddRange(backgroundJob, export);
        await context.SaveChangesAsync(cancellationToken);

        return new QueueDataExportResult(export.PublicId, backgroundJob.Id);
    }

    public async Task<DataExportWorkItem?> StartAsync(
        Guid exportId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var export = await context.DataExports
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.PublicId == exportId && item.Kind == DataExportKind.DataSet,
                cancellationToken);
        if (export is null || !await BeginAsync(export, startedAt, cancellationToken))
        {
            return null;
        }

        var specification = ReadSpecification(export.RequestJson);
        if (specification is null)
        {
            // An unreadable stored request can never be built; record why instead of retrying.
            export.Fail(DataExportMessages.RequestInvalid, startedAt);
            await context.SaveChangesAsync(cancellationToken);

            return null;
        }

        return new DataExportWorkItem(
            export.PublicId,
            export.User.PublicId,
            specification,
            export.FileName);
    }

    public async Task<JobTransitionOutcome> CompleteAsync(
        Guid exportId,
        int rowCount,
        string contentType,
        string storageKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var export = await FindAsync(exportId, cancellationToken);
        if (export is null)
        {
            return JobTransitionOutcome.NotFound;
        }

        if (export.Status != DataExportStatus.Running)
        {
            return JobTransitionOutcome.NotRunning;
        }

        export.Complete(rowCount, contentType, storageKey, completedAt);
        await context.SaveChangesAsync(cancellationToken);

        return JobTransitionOutcome.Applied;
    }

    public async Task<JobTransitionOutcome> FailAsync(
        Guid exportId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var export = await FindAsync(exportId, cancellationToken);
        if (export is null)
        {
            return JobTransitionOutcome.NotFound;
        }

        if (export.Status is not (DataExportStatus.Pending or DataExportStatus.Running))
        {
            return JobTransitionOutcome.NotRunning;
        }

        export.Fail(reason, failedAt);
        await context.SaveChangesAsync(cancellationToken);

        return JobTransitionOutcome.Applied;
    }

    public Task<DataExportReadSnapshot?> FindOwnedAsync(
        Guid userId,
        Guid exportId,
        CancellationToken cancellationToken) => context.DataExports
        .AsNoTracking()
        .Where(export => export.User.PublicId == userId &&
                         export.PublicId == exportId &&
                         export.Kind == DataExportKind.DataSet)
        .Select(export => new DataExportReadSnapshot(
            export.PublicId,
            export.BackgroundJobId,
            export.Format,
            export.Status,
            export.FileName,
            export.RowCount,
            export.ContentType,
            export.StorageKey,
            export.FailureReason,
            export.CreatedAt,
            export.UpdatedAt,
            export.ExpiresAt))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<QueueDataExportResult> QueuePersonalAsync(
        QueuePersonalDataExportRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == request.UserId,
            cancellationToken);
        var export = new DataExport(
            user,
            DataExportFormat.Zip,
            "und",
            request.FileName,
            "{\"archiveSchemaVersion\":1}",
            request.RequestedAt,
            request.ExpiresAt,
            DataExportKind.PersonalArchive);
        var backgroundJob = BackgroundJob.Create(
            PersonalDataExportJob.Type,
            JsonSerializer.Serialize(
                new PersonalDataExportJobPayload(export.PublicId),
                JsonOptions),
            $"{PersonalDataExportJob.Type}:{export.PublicId:N}",
            request.CorrelationId,
            request.RequestedAt);
        export.AttachBackgroundJob(backgroundJob);
        context.AddRange(backgroundJob, export);
        await context.SaveChangesAsync(cancellationToken);

        return new QueueDataExportResult(export.PublicId, backgroundJob.Id);
    }

    public async Task<PersonalDataExportWorkItem?> StartPersonalAsync(
        Guid exportId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var export = await context.DataExports
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.PublicId == exportId && item.Kind == DataExportKind.PersonalArchive,
                cancellationToken);
        if (export is null || !await BeginAsync(export, startedAt, cancellationToken))
        {
            return null;
        }

        return new PersonalDataExportWorkItem(
            export.PublicId,
            export.User.PublicId,
            export.FileName,
            export.ExpiresAt);
    }

    public Task<DataExportReadSnapshot?> FindOwnedPersonalAsync(
        Guid userId,
        Guid exportId,
        CancellationToken cancellationToken) => context.DataExports
        .AsNoTracking()
        .Where(export => export.User.PublicId == userId &&
                         export.PublicId == exportId &&
                         export.Kind == DataExportKind.PersonalArchive)
        .Select(export => new DataExportReadSnapshot(
            export.PublicId,
            export.BackgroundJobId,
            export.Format,
            export.Status,
            export.FileName,
            export.RowCount,
            export.ContentType,
            export.StorageKey,
            export.FailureReason,
            export.CreatedAt,
            export.UpdatedAt,
            export.ExpiresAt))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<DataExport?> FindAsync(Guid exportId, CancellationToken cancellationToken) =>
        context.DataExports.SingleOrDefaultAsync(item => item.PublicId == exportId, cancellationToken);

    /// <summary>
    /// Starts a pending export. A running one was interrupted by a restart and resumes; a finished
    /// one is not worked on again.
    /// </summary>
    private async Task<bool> BeginAsync(
        DataExport export,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        if (export.Status == DataExportStatus.Running)
        {
            return true;
        }

        if (export.Status != DataExportStatus.Pending)
        {
            return false;
        }

        export.Start(startedAt);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static DataExportSpecification? ReadSpecification(string requestJson)
    {
        try
        {
            return JsonSerializer.Deserialize<DataExportSpecification>(requestJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
