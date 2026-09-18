using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PersonalDataExportJobHandler(
    IPersonalDataExportStore personalExports,
    IDataExportStore exports,
    IPersonalDataArchiveBuilder builder,
    IAttachmentStore storage,
    TimeProvider timeProvider) : IBackgroundJobHandler
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

        try
        {
            var built = await builder.BuildAsync(
                work.UserId,
                timeProvider.GetUtcNow(),
                work.ExpiresAt,
                cancellationToken);
            var archive = built.Archive ?? throw new InvalidOperationException(
                $"The personal data archive could not be built: {built.Outcome}.");
            var storageKey = $"exports/{work.UserId:N}/{work.ExportId:N}.zip";
            await using var content = new MemoryStream(archive.Content, writable: false);
            await storage.WriteAsync(storageKey, content, cancellationToken);
            await exports.CompleteAsync(
                work.ExportId,
                archive.RecordCount,
                "application/zip",
                storageKey,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return ProcessOutput.New;
        }
        catch (Exception)
        {
            await exports.FailAsync(
                work.ExportId,
                PersonalDataExportMessages.GenerationFailed,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
