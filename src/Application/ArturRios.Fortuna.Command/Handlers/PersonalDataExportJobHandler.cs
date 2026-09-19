using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PersonalDataExportJobHandler(
    IPersonalDataExportStore personalExports,
    IDataExportStore exports,
    IPersonalDataArchiveBuilder builder,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<PersonalDataExportJobHandler> logger) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => PersonalDataExportJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<PersonalDataExportJobPayload>(payload, out var job, JsonOptions))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        var work = await personalExports.StartPersonalAsync(
            job.ExportId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (work is null)
        {
            return ProcessOutput.New.WithError(PersonalDataExportMessages.NotFound);
        }

        string? storageKey = null;
        try
        {
            var built = await builder.BuildAsync(
                work.UserId,
                timeProvider.GetUtcNow(),
                work.ExpiresAt,
                cancellationToken);
            if (built.Outcome != PersonalDataArchiveOutcome.Built || built.Archive is null)
            {
                return await FailAsync(work.ExportId, Reason(built.Outcome));
            }

            storageKey = $"exports/{work.UserId:N}/{work.ExportId:N}.zip";
            await using (var content = new MemoryStream(built.Archive.Content, writable: false))
            {
                await storage.WriteAsync(storageKey, content, cancellationToken);
            }

            var completion = await exports.CompleteAsync(
                work.ExportId,
                built.Archive.RecordCount,
                "application/zip",
                storageKey,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (completion != JobTransitionOutcome.Applied)
            {
                // The archive holds personal data and nothing references it any more.
                await TryDeleteAsync(storageKey);

                return ProcessOutput.New.WithError(completion == JobTransitionOutcome.NotFound
                    ? PersonalDataExportMessages.NotFound
                    : PersonalDataExportMessages.NoLongerRunning);
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

            await FailAsync(work.ExportId, PersonalDataExportMessages.GenerationFailed);
            throw;
        }
    }

    private static string Reason(PersonalDataArchiveOutcome outcome) => outcome switch
    {
        PersonalDataArchiveOutcome.UserNotFound => PersonalDataExportMessages.ProfileNotFound,
        PersonalDataArchiveOutcome.AttachmentNotFound => PersonalDataExportMessages.AttachmentMissing,
        PersonalDataArchiveOutcome.StorageUnavailable => PersonalDataExportMessages.StorageUnavailable,
        _ => PersonalDataExportMessages.GenerationFailed
    };

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
            logger.LogWarning(exception, "Could not delete the unreferenced archive {StorageKey}", storageKey);
        }
    }
}
