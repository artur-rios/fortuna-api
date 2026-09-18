using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
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
        var parser = new StubParser(PdfInvoiceImportMessages.UnsupportedLayout);

        var result = await new PdfInvoiceImportJobHandler(store, parser, new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([PdfInvoiceImportMessages.UnsupportedLayout], result.Errors);
        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    [UnitFact]
    public async Task GivenSummaryMismatchAtImport_WhenExecuted_ThenSpecificReasonFailsTheJob()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Rejected(PdfInvoiceImportMessages.SummaryDoesNotReconcile)
        };

        var result = await new PdfInvoiceImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([PdfInvoiceImportMessages.SummaryDoesNotReconcile], result.Errors);
        Assert.Equal(PdfInvoiceImportMessages.SummaryDoesNotReconcile, store.FailedReason);
    }

    [UnitFact]
    public async Task GivenCardDeletedWhileRunning_WhenExecuted_ThenJobFailsAsTargetUnavailable()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Of(ImportCompletionOutcome.TargetUnavailable)
        };

        var result = await new PdfInvoiceImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([PdfInvoiceImportMessages.CreditCardUnavailable], result.Errors);
        Assert.Equal(PdfInvoiceImportMessages.CreditCardUnavailable, store.FailedReason);
    }

    [UnitFact]
    public async Task GivenImportJobErasedWhileRunning_WhenExecuted_ThenErrorIsReturnedWithoutFailingIt()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Of(ImportCompletionOutcome.JobNotFound)
        };

        var result = await new PdfInvoiceImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([ImportJobMessages.NotFound], result.Errors);
        Assert.Null(store.FailedReason);
    }

    [UnitFact]
    public async Task GivenMalformedPayload_WhenExecuted_ThenPayloadErrorIsReturned()
    {
        var store = new StubStore();

        var result = await new PdfInvoiceImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync("{broken", CancellationToken.None);

        Assert.Equal([BackgroundJobMessages.PayloadInvalid], result.Errors);
        Assert.Null(store.BegunJobId);
    }

    [UnitFact]
    public async Task GivenNonReconcilingInvoice_WhenExecuted_ThenDiscrepancyFailsTheJob()
    {
        var store = new StubStore();
        var reason = PdfInvoiceImportMessages.ReconciliationFailed(99m, 100m, -1m);
        var parser = new StubParser(reason);

        var result = await new PdfInvoiceImportJobHandler(store, parser, new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([reason], result.Errors);
        Assert.Equal(reason, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    private static PdfInvoiceImportJobPayload Payload() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3]);

    private sealed class StubParser(string? error = null) : IPdfInvoiceParser
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

        public PdfInvoiceParseResult Parse(byte[] content) => error is null
            ? PdfInvoiceParseResult.Success(Invoice)
            : PdfInvoiceParseResult.Failure(error);
    }

    private sealed class StubStore : IPdfInvoiceImportStore
    {
        public ImportCompletionResult CompletionResult { get; init; } =
            ImportCompletionResult.Completed;
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

        public Task<ImportCompletionResult> CompleteAsync(
            Guid importJobId, Guid userId, Guid creditCardId, ParsedPdfInvoice invoice,
            DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            CompletedJobId = importJobId;
            Invoice = invoice;

            return Task.FromResult(CompletionResult);
        }

        public Task<JobTransitionOutcome> FailAsync(
            Guid importJobId, string reason, DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailedReason = reason;

            return Task.FromResult(JobTransitionOutcome.Applied);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
