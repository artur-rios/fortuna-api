using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DataExportJobHandler(
    IDataExportStore exports,
    DataExportBuilder builder,
    IDataExportRenderer renderer,
    IAttachmentStore storage,
    TimeProvider timeProvider) : IBackgroundJobHandler
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

        try
        {
            var built = await builder.BuildAsync(
                work.UserId,
                work.Specification,
                int.MaxValue,
                cancellationToken);
            if (built.Error is not null)
            {
                throw new InvalidOperationException(built.Error);
            }

            var rendered = renderer.Render(built.Document!, work.Specification.Format);
            var storageKey = $"exports/{work.UserId:N}/{work.ExportId:N}.{rendered.Extension}";
            await using var content = new MemoryStream(rendered.Content, writable: false);
            await storage.WriteAsync(storageKey, content, cancellationToken);
            await exports.CompleteAsync(
                work.ExportId,
                built.TotalCount,
                rendered.ContentType,
                storageKey,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return ProcessOutput.New;
        }
        catch (Exception)
        {
            await exports.FailAsync(
                work.ExportId,
                DataExportMessages.GenerationFailed,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
