using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class PdfInvoiceImportJobHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 13, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenQueuedPdfJob_WhenExecuted_ThenInvoiceIsParsedAndCompleted()
    {
        var store = new StubStore();
        var parser = new StubParser();
        var payload = Payload();

        await new PdfInvoiceImportJobHandler(store, parser, new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        Assert.Equal(payload.ImportJobId, store.BegunJobId);
        Assert.Equal(payload.ImportJobId, store.CompletedJobId);
        Assert.Same(parser.Invoice, store.Invoice);
        Assert.Null(store.FailedReason);
    }

    [UnitFact]
    public async Task GivenUnsupportedLayout_WhenExecuted_ThenExactReasonFailsTheJob()
    {
        var store = new StubStore();
        var parser = new StubParser(new PdfInvoiceParseException(
            PdfInvoiceImportMessages.UnsupportedLayout));

        await Assert.ThrowsAsync<PdfInvoiceParseException>(() =>
            new PdfInvoiceImportJobHandler(store, parser, new FixedTimeProvider(Now))
                .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None));

        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    [UnitFact]
    public async Task GivenNonReconcilingInvoice_WhenExecuted_ThenDiscrepancyFailsTheJob()
    {
        var store = new StubStore();
        var reason = PdfInvoiceImportMessages.ReconciliationFailed(99m, 100m, -1m);
        var parser = new StubParser(new PdfInvoiceParseException(reason));

        await Assert.ThrowsAsync<PdfInvoiceParseException>(() =>
            new PdfInvoiceImportJobHandler(store, parser, new FixedTimeProvider(Now))
                .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None));

        Assert.Equal(reason, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    private static PdfInvoiceImportJobPayload Payload() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3]);

    private sealed class StubParser(Exception? exception = null) : IPdfInvoiceParser
    {
        public ParsedPdfInvoice Invoice { get; } = new(
            "Nubank credit card invoice",
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 8, 12),
            0m, 0m, 10m, 0m, 0m, 10m, 10m, 0m,
            [new(1, "{}", new DateOnly(2026, 8, 1), null, "Purchase", 10m,
                PdfInvoiceLineKind.Purchase)]);

        public ParsedPdfInvoice Parse(byte[] content)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Invoice;
        }
    }

    private sealed class StubStore : IPdfInvoiceImportStore
    {
        public Guid? BegunJobId { get; private set; }
        public Guid? CompletedJobId { get; private set; }
        public ParsedPdfInvoice? Invoice { get; private set; }
        public string? FailedReason { get; private set; }

        public Task<QueuePdfInvoiceImportResult> QueueAsync(
            PdfInvoiceImportRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            BegunJobId = importJobId;
            return Task.FromResult(true);
        }

        public Task CompleteAsync(
            Guid importJobId, Guid userId, Guid creditCardId, ParsedPdfInvoice invoice,
            DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            CompletedJobId = importJobId;
            Invoice = invoice;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid importJobId, string reason, DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailedReason = reason;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
