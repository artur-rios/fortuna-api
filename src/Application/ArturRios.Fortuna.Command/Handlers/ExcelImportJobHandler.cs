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
            IReadOnlyCollection<ExcelWorkbookRow> rows;
            try
            {
                rows = parser.Parse(request.Content, request.Mapping);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await FailAsync(request.ImportJobId, ExcelImportMessages.WorkbookInvalid);
            }

            var completion = await imports.CompleteAsync(
                request.ImportJobId,
                request.UserId,
                request.TargetId,
                request.TargetType,
                request.CreateMissingCategories,
                rows,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return completion.Outcome switch
            {
                ImportCompletionOutcome.Completed => ProcessOutput.New,
                ImportCompletionOutcome.JobNotFound => ProcessOutput.New.WithError(ImportJobMessages.NotFound),
                ImportCompletionOutcome.JobNotRunning =>
                    ProcessOutput.New.WithError(ImportJobMessages.NoLongerRunning),
                ImportCompletionOutcome.TargetUnavailable =>
                    await FailAsync(request.ImportJobId, ExcelImportMessages.TargetUnavailable),
                _ => await FailAsync(
                    request.ImportJobId,
                    completion.Reason ?? ImportJobMessages.ProcessingFailed)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            // Unexpected (for example a database failure): leave the import job failed rather than
            // running forever, then let the processor record the defect.
            await FailAsync(request.ImportJobId, ImportJobMessages.ProcessingFailed);
            throw;
        }
    }

    private async Task<ProcessOutput> FailAsync(Guid importJobId, string reason)
    {
        // The outcome is decided; host shutdown must not leave the import job running.
        await imports.FailAsync(importJobId, reason, timeProvider.GetUtcNow(), CancellationToken.None);

        return ProcessOutput.New.WithError(reason);
    }
}
