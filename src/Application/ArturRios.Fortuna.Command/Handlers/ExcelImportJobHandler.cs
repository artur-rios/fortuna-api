using System.Text.Json;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ExcelImportJobHandler(
    IExcelImportStore imports,
    IExcelWorkbookParser parser,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => ExcelImportJob.Type;

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ExcelImportJobPayload>(payload)
            ?? throw new InvalidOperationException("The Excel import payload is invalid.");
        if (!await imports.BeginAsync(
            request.ImportJobId,
            timeProvider.GetUtcNow(),
            cancellationToken))
        {
            throw new InvalidOperationException("The Excel import job was not found.");
        }

        try
        {
            var rows = parser.Parse(request.Content, request.Mapping);
            await imports.CompleteAsync(
                request.ImportJobId,
                request.UserId,
                request.TargetId,
                request.TargetType,
                request.CreateMissingCategories,
                rows,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await imports.FailAsync(
                request.ImportJobId,
                ExcelImportMessages.WorkbookInvalid,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
