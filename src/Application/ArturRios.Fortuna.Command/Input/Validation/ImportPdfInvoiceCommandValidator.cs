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
            .WithMessage(PdfInvoiceImportMessages.CreditCardIdRequired);
        RuleFor(command => command.Content)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(PdfInvoiceImportMessages.FileRequired)
            .Must(content => content.Length <= options.MaximumFileBytes)
            .WithMessage(PdfInvoiceImportMessages.FileTooLarge)
            .Must(FileSignatures.IsPdf)
            .WithMessage(PdfInvoiceImportMessages.FileInvalid);
        RuleFor(command => command.FileName)
            .TrimmedMaximumLength(300)
            .WithMessage(PdfInvoiceImportMessages.FileNameTooLong);
    }
}
