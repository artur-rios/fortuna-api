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
            var invoice = parser.Parse(request.Content);
            await imports.CompleteAsync(
                request.ImportJobId,
                request.UserId,
                request.CreditCardId,
                invoice,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return ProcessOutput.New;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PdfInvoiceParseException exception)
        {
            await imports.FailAsync(
                request.ImportJobId,
                exception.Message,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
        catch (Exception)
        {
            await imports.FailAsync(
                request.ImportJobId,
                PdfInvoiceImportMessages.FileInvalid,
                timeProvider.GetUtcNow(),
                cancellationToken);
            throw;
        }
    }
}
