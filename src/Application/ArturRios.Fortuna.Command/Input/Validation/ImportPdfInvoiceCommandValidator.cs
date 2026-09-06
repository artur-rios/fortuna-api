using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class ImportPdfInvoiceCommandValidator : AbstractValidator<ImportPdfInvoiceCommand>
{
    public ImportPdfInvoiceCommandValidator(PdfInvoiceImportOptions options)
    {
        RuleFor(command => command.CreditCardId)
            .NotEmpty()
            .WithMessage(PdfInvoiceImportMessages.CreditCardNotFound);
        RuleFor(command => command.Content)
            .NotEmpty()
            .WithMessage(PdfInvoiceImportMessages.FileRequired)
            .Must(content => content.Length <= options.MaximumFileBytes)
            .WithMessage(PdfInvoiceImportMessages.FileTooLarge);
    }
}
