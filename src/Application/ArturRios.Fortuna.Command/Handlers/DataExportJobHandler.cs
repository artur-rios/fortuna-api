using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DataExportJobHandler(
    IDataExportStore exports,
    DataExportBuilder builder,
    IDataExportRenderer renderer,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<DataExportJobHandler> logger) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => DataExportJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<DataExportJobPayload>(payload, out var job, JsonOptions))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        var work = await exports.StartAsync(
            job.ExportId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (work is null)
        {
            return ProcessOutput.New.WithError(DataExportMessages.NotFound);
        }

        string? storageKey = null;
        try
        {
            var built = await builder.BuildAsync(
                work.UserId,
                work.Specification,
                int.MaxValue,
                cancellationToken);
            if (built.Error is not null || built.Document is null)
            {
                // The builder's reason (unknown column, unsupported currency, ...) is what the
                // user needs to fix the request, so it is recorded instead of a generic failure.
                return await FailAsync(work.ExportId, built.Error ?? DataExportMessages.GenerationFailed);
            }

            var rendered = renderer.Render(built.Document, work.Specification.Format);
            storageKey = $"exports/{work.UserId:N}/{work.ExportId:N}.{rendered.Extension}";
            await using (var content = new MemoryStream(rendered.Content, writable: false))
            {
                await storage.WriteAsync(storageKey, content, cancellationToken);
            }

            var completion = await exports.CompleteAsync(
                work.ExportId,
                built.TotalCount,
                rendered.ContentType,
                storageKey,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (completion != JobTransitionOutcome.Applied)
            {
                // Nothing references the file any more; do not leave it orphaned in storage.
                await TryDeleteAsync(storageKey);

                return ProcessOutput.New.WithError(completion == JobTransitionOutcome.NotFound
                    ? DataExportMessages.NotFound
                    : DataExportMessages.NoLongerRunning);
            }

            return ProcessOutput.New;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: the job is requeued and resumes the running export, overwriting the
            // same storage key, so neither the export nor the file is touched here.
            throw;
        }
        catch (Exception)
        {
            if (storageKey is not null)
            {
                await TryDeleteAsync(storageKey);
            }

            await FailAsync(work.ExportId, DataExportMessages.GenerationFailed);
            throw;
        }
    }

    private async Task<ProcessOutput> FailAsync(Guid exportId, string reason)
    {
        // The outcome is decided; host shutdown must not leave the export running.
        await exports.FailAsync(exportId, reason, timeProvider.GetUtcNow(), CancellationToken.None);

        return ProcessOutput.New.WithError(reason);
    }

    private async Task TryDeleteAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Cleanup is best effort; the export's failure is what gets reported.
            logger.LogWarning(exception, "Could not delete the unreferenced export file {StorageKey}", storageKey);
        }
    }
}
