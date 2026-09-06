using ArturRios.Fortuna.Domain.Ingestion;

namespace ArturRios.Fortuna.Shared.Ingestion;

public sealed record PdfInvoiceImportRequest(
    Guid UserId,
    Guid CreditCardId,
    string FileName,
    byte[] Content,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

public enum QueuePdfInvoiceImportOutcome
{
    Succeeded = 1,
    CreditCardNotFound = 2,
    CreditCardDeleted = 3
}

public sealed record QueuePdfInvoiceImportResult(
    PdfInvoiceImportJobSnapshot? Job,
    Guid? BackgroundJobId,
    QueuePdfInvoiceImportOutcome Outcome);

public sealed record PdfInvoiceImportJobSnapshot(
    Guid Id,
    ImportJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IPdfInvoiceImportStore
{
    Task<QueuePdfInvoiceImportResult> QueueAsync(
        PdfInvoiceImportRequest request,
        CancellationToken cancellationToken);

    Task<bool> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid importJobId,
        Guid userId,
        Guid creditCardId,
        ParsedPdfInvoice invoice,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid importJobId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

public interface IPdfInvoiceParser
{
    ParsedPdfInvoice Parse(byte[] content);
}

public enum PdfInvoiceLineKind
{
    Purchase = 1,
    Tax = 2,
    TaxReversal = 3,
    PurchaseReversal = 4,
    CreditAdjustment = 5,
    Payment = 6
}

public sealed record ParsedPdfInvoiceLine(
    int Sequence,
    string RawPayload,
    DateOnly OccurredOn,
    string? MaskedCardNumber,
    string Description,
    decimal SignedAmount,
    PdfInvoiceLineKind Kind,
    short? InstallmentNumber = null,
    short? InstallmentCount = null,
    decimal? OriginalAmount = null,
    string? OriginalCurrencyCode = null,
    decimal? AppliedRate = null,
    int? RelatedLineSequence = null,
    string? OriginalPurchaseReference = null,
    bool IsUnmatchedReference = false);

public sealed record ParsedPdfInvoice(
    string Layout,
    DateOnly DueDate,
    DateOnly IssueDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal PreviousBalance,
    decimal PaymentsReceived,
    decimal PurchaseTotal,
    decimal ForeignTaxTotal,
    decimal OtherEntries,
    decimal AmountDue,
    decimal ParsedAmountDue,
    decimal ReconciliationDifference,
    IReadOnlyCollection<ParsedPdfInvoiceLine> Lines);

public sealed class PdfInvoiceParseException(string message) : Exception(message);

public static class PdfInvoiceImportJob
{
    public const string Type = "pdf-invoice-import";
}

public sealed record PdfInvoiceImportJobPayload(
    Guid ImportJobId,
    Guid UserId,
    Guid CreditCardId,
    byte[] Content);

public sealed record PdfInvoiceImportOptions(int MaximumFileBytes);
