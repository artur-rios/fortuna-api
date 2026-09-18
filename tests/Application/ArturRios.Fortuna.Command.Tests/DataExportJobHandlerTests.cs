using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class DataExportJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExportId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [UnitFact]
    public async Task GivenQueuedExport_WhenProcessed_ThenFileIsStoredAndExportCompletes()
    {
        var store = new StubExportStore();
        var storage = new MemoryStorage();

        var result = await Handler(store, storage).ExecuteAsync(Payload(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"exports/{UserId:N}/{ExportId:N}.csv", store.CompletedKey);
        Assert.NotNull(storage.WrittenKey);
        Assert.Null(storage.DeletedKey);
        Assert.Null(store.FailureReason);
    }

    [UnitFact]
    public async Task GivenBuilderRejectsTheRequest_WhenProcessed_ThenItsOwnReasonFailsTheExport()
    {
        var store = new StubExportStore();
        var storage = new MemoryStorage();

        var result = await Handler(store, storage, TableReportReadOutcome.ColumnUnknown)
            .ExecuteAsync(Payload(), CancellationToken.None);

        var expected = TableReportMessages.UnknownColumn("transactions", "bad");
        Assert.Equal([expected], result.Errors);
        Assert.Equal(expected, store.FailureReason);
        Assert.Equal(CancellationToken.None, store.FailToken);
        Assert.Null(storage.WrittenKey);
    }

    [UnitTheory]
    [InlineData(JobTransitionOutcome.NotFound, DataExportMessages.NotFound)]
    [InlineData(JobTransitionOutcome.NotRunning, DataExportMessages.NoLongerRunning)]
    public async Task GivenExportGoneBeforeCompletion_WhenProcessed_ThenWrittenFileIsDeleted(
        JobTransitionOutcome outcome,
        string reason)
    {
        var store = new StubExportStore { CompleteOutcome = outcome };
        var storage = new MemoryStorage();

        var result = await Handler(store, storage).ExecuteAsync(Payload(), CancellationToken.None);

        Assert.Equal([reason], result.Errors);
        Assert.Equal(storage.WrittenKey, storage.DeletedKey);
    }

    [UnitFact]
    public async Task GivenCompletionThrows_WhenProcessed_ThenFileIsDeletedAndExportFails()
    {
        var store = new StubExportStore { CompleteFailure = new IOException("database unavailable") };
        var storage = new MemoryStorage();

        await Assert.ThrowsAsync<IOException>(() =>
            Handler(store, storage).ExecuteAsync(Payload(), CancellationToken.None));

        Assert.Equal(storage.WrittenKey, storage.DeletedKey);
        Assert.Equal(DataExportMessages.GenerationFailed, store.FailureReason);
        Assert.Equal(CancellationToken.None, store.FailToken);
    }

    [UnitFact]
    public async Task GivenHostShutdown_WhenProcessed_ThenCancellationPropagatesWithoutFailingTheExport()
    {
        var store = new StubExportStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var storage = new MemoryStorage { Cancellation = cancellation.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Handler(store, storage).ExecuteAsync(Payload(), cancellation.Token));

        Assert.Null(store.FailureReason);
        Assert.Null(storage.DeletedKey);
    }

    [UnitFact]
    public async Task GivenMissingExport_WhenProcessed_ThenNotFoundIsReturned()
    {
        var store = new StubExportStore { Work = null };

        var result = await Handler(store, new MemoryStorage()).ExecuteAsync(Payload(), CancellationToken.None);

        Assert.Equal([DataExportMessages.NotFound], result.Errors);
    }

    [UnitFact]
    public async Task GivenTotalWithoutFigureDate_WhenConverted_ThenTheInjectedClockPicksTheRateDate()
    {
        var rates = new RecordingRateReader();
        var builder = new DataExportBuilder(
            new StubTableReader(TableReportReadOutcome.Succeeded),
            new StubCurrencyReader(),
            rates,
            new FixedTimeProvider(Now));

        await builder.BuildAsync(UserId, Specification("USD"), int.MaxValue, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 8), rates.FigureDate);
    }

    private static DataExportJobHandler Handler(
        StubExportStore store,
        MemoryStorage storage,
        TableReportReadOutcome outcome = TableReportReadOutcome.Succeeded) => new(
        store,
        new DataExportBuilder(
            new StubTableReader(outcome),
            new StubCurrencyReader(),
            new RecordingRateReader(),
            new FixedTimeProvider(Now)),
        new StubRenderer(),
        storage,
        new FixedTimeProvider(Now),
        NullLogger<DataExportJobHandler>.Instance);

    private static string Payload() => JsonSerializer.Serialize(
        new DataExportJobPayload(ExportId),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static DataExportSpecification Specification(string? displayCurrency = null) => new(
        "transactions",
        ["amount"],
        [],
        [],
        displayCurrency,
        DataExportFormat.Csv,
        "en-US");

    private sealed class StubTableReader(TableReportReadOutcome outcome) : ITableReportReader
    {
        public Task<TableReportReadResult> ReadAsync(
            TableReportCriteria criteria,
            CancellationToken cancellationToken)
        {
            if (outcome != TableReportReadOutcome.Succeeded)
            {
                return Task.FromResult(new TableReportReadResult(outcome, InvalidName: "bad"));
            }

            IReadOnlyCollection<IReadOnlyDictionary<string, object?>> rows =
            [
                new Dictionary<string, object?> { ["amount"] = 12.34m, ["currencyCode"] = "BRL" }
            ];

            return Task.FromResult(new TableReportReadResult(
                TableReportReadOutcome.Succeeded,
                new TableReportSnapshot(
                    "transactions",
                    [new TableColumnSnapshot("amount", TableColumnType.Decimal, true, "currencyCode")],
                    rows,
                    1,
                    1,
                    criteria.PageSize,
                    [new TableTotalGroupSnapshot("amount", "BRL", null, 12.34m)])));
        }
    }

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<CurrencySnapshot?>(new CurrencySnapshot(code, code, 2));
    }

    private sealed class RecordingRateReader : IExchangeRateReader
    {
        public DateOnly? FigureDate { get; private set; }

        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken)
        {
            FigureDate = figureDate;

            return Task.FromResult<ExchangeRateSnapshot?>(null);
        }
    }

    private sealed class StubRenderer : IDataExportRenderer
    {
        public RenderedDataExport Render(DataExportDocument document, DataExportFormat format) =>
            new([1, 2, 3], "text/csv", "csv");
    }

    private sealed class StubExportStore : IDataExportStore
    {
        public DataExportWorkItem? Work { get; init; } =
            new(ExportId, UserId, Specification(), "export.csv");
        public JobTransitionOutcome CompleteOutcome { get; init; } = JobTransitionOutcome.Applied;
        public Exception? CompleteFailure { get; init; }
        public string? CompletedKey { get; private set; }
        public string? FailureReason { get; private set; }
        public CancellationToken? FailToken { get; private set; }

        public Task<QueueDataExportResult> QueueAsync(
            QueueDataExportRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DataExportWorkItem?> StartAsync(
            Guid exportId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => Task.FromResult(Work);

        public Task<JobTransitionOutcome> CompleteAsync(
            Guid exportId,
            int rowCount,
            string contentType,
            string storageKey,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            if (CompleteFailure is not null)
            {
                return Task.FromException<JobTransitionOutcome>(CompleteFailure);
            }

            CompletedKey = storageKey;

            return Task.FromResult(CompleteOutcome);
        }

        public Task<JobTransitionOutcome> FailAsync(
            Guid exportId,
            string reason,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailureReason = reason;
            FailToken = cancellationToken;

            return Task.FromResult(JobTransitionOutcome.Applied);
        }
    }

    private sealed class MemoryStorage : IAttachmentStore
    {
        public CancellationToken? Cancellation { get; init; }
        public string? WrittenKey { get; private set; }
        public string? DeletedKey { get; private set; }

        public Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            Cancellation?.ThrowIfCancellationRequested();
            WrittenKey = key;

            return Task.CompletedTask;
        }

        public Task<AttachmentReadResult> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            DeletedKey = key;

            return Task.CompletedTask;
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
