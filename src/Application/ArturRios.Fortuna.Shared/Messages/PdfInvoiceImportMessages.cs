using System.Globalization;

namespace ArturRios.Fortuna.Shared.Messages;

public static class PdfInvoiceImportMessages
{
    public const string Accepted = "PDF invoice import queued successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string CreditCardNotFound = "The target credit card was not found.";
    public const string CreditCardDeleted = "The target credit card is deleted.";
    public const string FileRequired = "A PDF invoice file is required.";
    public const string FileTooLarge = "The PDF invoice exceeds the configured size limit.";
    public const string FileInvalid = "The file is not a readable PDF.";
    public const string NoTextLayer = "The PDF has no text layer; OCR is not supported.";
    public const string UnsupportedLayout =
        "The PDF layout was not recognized. Supported layouts: Nubank credit card invoice.";
    public const string InvoiceIncomplete = "The Nubank invoice is missing required header or summary fields.";

    public static string ReconciliationFailed(
        decimal parsedAmountDue,
        decimal statedAmountDue,
        decimal difference) => string.Format(
            CultureInfo.InvariantCulture,
            "The invoice does not reconcile: parsed amount due {0:F2}, stated amount due {1:F2}, discrepancy {2:F2}.",
            parsedAmountDue,
            statedAmountDue,
            difference);
}
