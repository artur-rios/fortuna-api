using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ImportPdfInvoiceCommandHandler(
    IValidator<ImportPdfInvoiceCommand> validator,
    ICurrentProfileResolver profileResolver,
    IPdfInvoiceImportStore imports,
    IBackgroundJobQueue queue,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<ImportPdfInvoiceCommand, ImportPdfInvoiceCommandOutput>
{
    public async Task<DataOutput<ImportPdfInvoiceCommandOutput?>> HandleAsync(
        ImportPdfInvoiceCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<ImportPdfInvoiceCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<ImportPdfInvoiceCommandOutput?>.New.WithError(
                PdfInvoiceImportMessages.ProfileNotFound);
        }

        var result = await imports.QueueAsync(new PdfInvoiceImportRequest(
            profile.Id,
            command.CreditCardId,
            command.FileName.Trim(),
            command.Content,
            command.CorrelationId,
            timeProvider.GetUtcNow()), CancellationToken.None);
        if (result.Outcome == QueuePdfInvoiceImportOutcome.Succeeded)
        {
            await queue.EnqueueAsync(result.BackgroundJobId!.Value, CancellationToken.None);
        }

        return Resolve(result);
    }

    private static DataOutput<ImportPdfInvoiceCommandOutput?> Resolve(
        QueuePdfInvoiceImportResult result)
    {
        var output = DataOutput<ImportPdfInvoiceCommandOutput?>.New;
        if (result.Job is not null)
        {
            output = output.WithData(new ImportPdfInvoiceCommandOutput
            {
                ImportJobId = result.Job.Id,
                Status = result.Job.Status
            });
        }

        return result.Outcome switch
        {
            QueuePdfInvoiceImportOutcome.Succeeded => output.WithMessage(
                PdfInvoiceImportMessages.Accepted),
            QueuePdfInvoiceImportOutcome.CreditCardNotFound => output.WithError(
                PdfInvoiceImportMessages.CreditCardNotFound),
            QueuePdfInvoiceImportOutcome.CreditCardDeleted => output.WithError(
                PdfInvoiceImportMessages.CreditCardDeleted),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
