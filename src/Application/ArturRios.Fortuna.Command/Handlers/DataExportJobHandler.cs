using System.Text.Json;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;

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

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<DataExportJobPayload>(payload, JsonOptions) ??
            throw new InvalidOperationException("The export job payload is invalid.");
        var work = await exports.StartAsync(
            job.ExportId,
            timeProvider.GetUtcNow(),
            cancellationToken) ?? throw new InvalidOperationException("The export was not found.");

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
        }
        catch (Exception exception)
        {
            var reason = exception.Message.Length <= 1000
                ? exception.Message
                : exception.Message[..1000];
            await exports.FailAsync(
                work.ExportId,
                reason,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
