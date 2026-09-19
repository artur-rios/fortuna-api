using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.WebApi.Requests;

public enum UploadKind
{
    Attachment,
    ExcelImport,
    PdfInvoiceImport
}

/// <summary>
///     The configured size limit of each upload endpoint, in one place: the request filter caps the
///     body with it and the controllers check the file against it before buffering.
/// </summary>
public sealed class UploadLimits(
    AttachmentOptions attachments,
    ExcelImportOptions excelImports,
    PdfInvoiceImportOptions pdfInvoiceImports)
{
    /// <summary>
    ///     Room for the multipart boundaries, part headers and the other form fields around the
    ///     file, so a file exactly at the limit is still accepted.
    /// </summary>
    public const long MultipartOverheadBytes = 64 * 1024;

    public long MaximumFileBytes(UploadKind kind) => kind switch
    {
        UploadKind.Attachment => attachments.MaximumBytes,
        UploadKind.ExcelImport => excelImports.MaximumFileBytes,
        UploadKind.PdfInvoiceImport => pdfInvoiceImports.MaximumFileBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public long MaximumRequestBytes(UploadKind kind) => MaximumFileBytes(kind) + MultipartOverheadBytes;

    public string FileTooLarge(UploadKind kind) => kind switch
    {
        UploadKind.Attachment => AttachmentMessages.FileTooLarge(attachments.MaximumBytes),
        UploadKind.ExcelImport => ExcelImportMessages.FileTooLarge,
        UploadKind.PdfInvoiceImport => PdfInvoiceImportMessages.FileTooLarge,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
