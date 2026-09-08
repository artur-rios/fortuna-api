using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Exports;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Exports;

public sealed class EfDataExportStore(AppDbContext context) : IDataExportStore
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
            .SingleOrDefaultAsync(item => item.PublicId == exportId, cancellationToken);
        if (export is null)
        {
            return null;
        }

        export.Start(startedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new DataExportWorkItem(
            export.PublicId,
            export.User.PublicId,
            JsonSerializer.Deserialize<DataExportSpecification>(
                export.RequestJson,
                JsonOptions) ?? throw new InvalidOperationException(
                    "The stored export request is invalid."),
            export.FileName);
    }

    public async Task CompleteAsync(
        Guid exportId,
        int rowCount,
        string contentType,
        string storageKey,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var export = await RequiredAsync(exportId, cancellationToken);
        export.Complete(rowCount, contentType, storageKey, completedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid exportId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var export = await RequiredAsync(exportId, cancellationToken);
        export.Fail(reason.Length <= 1000 ? reason : reason[..1000], failedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<DataExport> RequiredAsync(
        Guid exportId,
        CancellationToken cancellationToken) =>
        await context.DataExports.SingleOrDefaultAsync(
            item => item.PublicId == exportId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The export record was not found.");
}
