using System.Text.Json;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class PdfInvoiceImportJobHandler(
    IPdfInvoiceImportStore imports,
    IPdfInvoiceParser parser,
    TimeProvider timeProvider) : IBackgroundJobHandler
{
    public string JobType => PdfInvoiceImportJob.Type;

    public async Task ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<PdfInvoiceImportJobPayload>(payload)
            ?? throw new InvalidOperationException("The PDF invoice import payload is invalid.");
        if (!await imports.BeginAsync(
            request.ImportJobId,
            timeProvider.GetUtcNow(),
            cancellationToken))
        {
            throw new InvalidOperationException("The PDF invoice import job was not found.");
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
