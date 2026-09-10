using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;

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

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<PersonalDataExportJobPayload>(payload, JsonOptions) ??
            throw new InvalidOperationException("The personal data export job payload is invalid.");
        var work = await personalExports.StartPersonalAsync(
            job.ExportId,
            timeProvider.GetUtcNow(),
            cancellationToken) ?? throw new InvalidOperationException(
                "The personal data export was not found.");

        try
        {
            var archive = await builder.BuildAsync(
                work.UserId,
                timeProvider.GetUtcNow(),
                work.ExpiresAt,
                cancellationToken);
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
