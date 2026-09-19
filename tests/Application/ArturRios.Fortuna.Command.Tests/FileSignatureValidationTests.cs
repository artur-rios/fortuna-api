using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class FileSignatureValidationTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] ZipBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];

    [UnitFact]
    public async Task GivenNonPdfBytes_WhenPdfImportValidated_ThenFileIsRejectedAsInvalid()
    {
        var result = await new ImportPdfInvoiceCommandValidator(new PdfInvoiceImportOptions(1024))
            .ValidateAsync(new ImportPdfInvoiceCommand
            {
                CreditCardId = Guid.NewGuid(),
                FileName = "invoice.pdf",
                Content = ZipBytes
            });

        var error = Assert.Single(result.Errors);
        Assert.Equal(PdfInvoiceImportMessages.FileInvalid, error.ErrorMessage);
    }

    [UnitFact]
    public async Task GivenPdfBytes_WhenExcelImportValidated_ThenWorkbookIsRejectedAsInvalid()
    {
        var result = await new ImportExcelWorkbookCommandValidator(new ExcelImportOptions(1024))
            .ValidateAsync(new ImportExcelWorkbookCommand
            {
                TargetId = Guid.NewGuid(),
                TargetType = ImportTargetType.Account,
                FileName = "sheet.xlsx",
                Content = PdfBytes,
                Mapping = new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null)
            });

        var error = Assert.Single(result.Errors);
        Assert.Equal(ExcelImportMessages.WorkbookInvalid, error.ErrorMessage);
    }

    [UnitTheory]
    [InlineData("application/pdf", "pdf", true)]
    [InlineData("application/pdf", "png", false)]
    [InlineData("image/png", "png", true)]
    [InlineData("image/png", "jpeg", false)]
    [InlineData("IMAGE/JPEG", "jpeg", true)]
    [InlineData("image/jpeg", "pdf", false)]
    [InlineData("text/plain", "pdf", true)]
    public async Task GivenDeclaredType_WhenAttachmentValidated_ThenMagicBytesMustMatch(
        string contentType,
        string actual,
        bool expected)
    {
        var content = actual switch
        {
            "pdf" => PdfBytes,
            "png" => PngBytes,
            _ => JpegBytes
        };
        var validator = new AttachDocumentCommandValidator(new AttachmentOptions(
            1024,
            ["application/pdf", "image/png", "image/jpeg", "text/plain"]));

        var result = await validator.ValidateAsync(new AttachDocumentCommand
        {
            TransactionId = Guid.NewGuid(),
            FileName = "document",
            ContentType = contentType,
            Content = content
        });

        Assert.Equal(expected, result.IsValid);
        if (!expected)
        {
            Assert.Equal(
                AttachmentMessages.ContentDoesNotMatchType,
                Assert.Single(result.Errors).ErrorMessage);
        }
    }
}
