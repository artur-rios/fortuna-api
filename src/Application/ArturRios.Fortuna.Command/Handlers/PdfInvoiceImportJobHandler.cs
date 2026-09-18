using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PdfInvoiceImportJobHandler(
    IPdfInvoiceImportStore imports,
    IPdfInvoiceParser parser,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => PdfInvoiceImportJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<PdfInvoiceImportJobPayload>(payload, out var request))
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
            ParsedPdfInvoice invoice;
            try
            {
                invoice = parser.Parse(request.Content);
            }
            catch (PdfInvoiceParseException exception)
            {
                return await FailAsync(request.ImportJobId, exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await FailAsync(request.ImportJobId, PdfInvoiceImportMessages.FileInvalid);
            }

            var completion = await imports.CompleteAsync(
                request.ImportJobId,
                request.UserId,
                request.CreditCardId,
                invoice,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return completion.Outcome switch
            {
                ImportCompletionOutcome.Completed => ProcessOutput.New,
                ImportCompletionOutcome.JobNotFound => ProcessOutput.New.WithError(ImportJobMessages.NotFound),
                ImportCompletionOutcome.JobNotRunning =>
                    ProcessOutput.New.WithError(ImportJobMessages.NoLongerRunning),
                ImportCompletionOutcome.TargetUnavailable =>
                    await FailAsync(request.ImportJobId, PdfInvoiceImportMessages.CreditCardUnavailable),
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
