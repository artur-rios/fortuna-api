using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ExcelImportJobHandler(
    IExcelImportStore imports,
    IExcelWorkbookParser parser,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => ExcelImportJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<ExcelImportJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        if (!await imports.BeginAsync(
            request.ImportJobId,
            timeProvider.GetUtcNow(),
            cancellationToken))
        {
            return ProcessOutput.New.WithError(ImportJobMessages.NotFound);
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

            return ProcessOutput.New;
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
