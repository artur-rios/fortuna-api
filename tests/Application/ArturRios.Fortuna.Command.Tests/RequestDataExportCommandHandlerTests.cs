using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RequestDataExportCommandHandlerTests
{
    private static readonly Guid ExportId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly UserProfileSnapshot Profile = new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false,
        DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    [UnitFact]
    public async Task GivenSmallResult_WhenExportRequested_ThenLocalizedFileIsReturnedDirectly()
    {
        var context = Context(totalCount: 1, threshold: 1);

        var result = await context.Handler.HandleAsync(Command("csv"));

        Assert.True(result.Success);
        Assert.Equal(DataExportDelivery.Direct, result.Data!.Delivery);
        Assert.Equal([1, 2, 3], result.Data.Content);
        Assert.Contains("currencyCode", context.Reader.LastColumns!);
        Assert.False(context.Queue.WasEnqueued);
    }

    [UnitFact]
    public async Task GivenLargeResult_WhenExportRequested_ThenOwnedJobIsQueued()
    {
        var context = Context(totalCount: 2, threshold: 1);

        var result = await context.Handler.HandleAsync(Command("xlsx"));

        Assert.True(result.Success);
        Assert.Equal(DataExportDelivery.Queued, result.Data!.Delivery);
        Assert.Equal(ExportId, result.Data.ExportId);
        Assert.Equal(JobId, result.Data.JobId);
        Assert.Equal(JobId, context.Queue.JobId);
        Assert.Equal(Profile.Id, context.Store.Request!.UserId);
    }

    [UnitFact]
    public async Task GivenInvalidFormatOrLocale_WhenExportRequested_ThenValidationNamesFormats()
    {
        var context = Context(totalCount: 0, threshold: 1);
        var command = Command("xml");
        command.Locale = "invalid-locale";

        var result = await context.Handler.HandleAsync(command);

        Assert.Contains(DataExportMessages.FormatUnsupported, result.Errors);
        Assert.Contains(DataExportMessages.LocaleInvalid, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfileOrUnknownColumn_WhenExportRequested_ThenItIsRejected()
    {
        var missing = Context(totalCount: 0, threshold: 1, missingProfile: true);
        var invalid = Context(
            totalCount: 0,
            threshold: 1,
            outcome: TableReportReadOutcome.ColumnUnknown);

        var missingResult = await missing.Handler.HandleAsync(Command("pdf"));
        var invalidResult = await invalid.Handler.HandleAsync(Command("pdf"));

        Assert.Contains(DataExportMessages.ProfileNotFound, missingResult.Errors);
        Assert.Contains(TableReportMessages.UnknownColumn("transactions", "bad"),
            invalidResult.Errors);
    }

    private static TestContext Context(
        int totalCount,
        int threshold,
        bool missingProfile = false,
        TableReportReadOutcome outcome = TableReportReadOutcome.Succeeded)
    {
        var reader = new StubTableReader(totalCount, outcome);
        var store = new StubExportStore();
        var queue = new StubQueue();
        var builder = new DataExportBuilder(
            reader,
            new StubCurrencyReader(),
            new StubRateReader());
        var handler = new RequestDataExportCommandHandler(
            new RequestDataExportCommandValidator(),
            new StubActor(),
            new StubProfileReader(missingProfile ? null : Profile),
            builder,
            new StubRenderer(),
            store,
            queue,
            new DataExportOptions(threshold, TimeSpan.FromHours(24), "pt-BR"),
            new FixedTimeProvider());
        return new TestContext(handler, reader, store, queue);
    }

    private static RequestDataExportCommand Command(string format) => new()
    {
        RecordSet = "transactions",
        Columns = ["amount"],
        Format = format,
        Locale = "pt-BR"
    };

    private sealed record TestContext(
        RequestDataExportCommandHandler Handler,
        StubTableReader Reader,
        StubExportStore Store,
        StubQueue Queue);

    private sealed class StubTableReader(
        int totalCount,
        TableReportReadOutcome outcome) : ITableReportReader
    {
        public IReadOnlyCollection<string>? LastColumns { get; private set; }

        public Task<TableReportReadResult> ReadAsync(
            TableReportCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastColumns = criteria.Columns;
            if (outcome != TableReportReadOutcome.Succeeded)
            {
                return Task.FromResult(new TableReportReadResult(
                    outcome, InvalidName: "bad"));
            }

            var columns = criteria.Columns.Select(name => name == "amount"
                ? new TableColumnSnapshot(name, TableColumnType.Decimal, true, "currencyCode")
                : new TableColumnSnapshot(name, TableColumnType.Text, false, null)).ToArray();
            IReadOnlyCollection<IReadOnlyDictionary<string, object?>> rows = totalCount == 0
                ? []
                : [new Dictionary<string, object?>
                {
                    ["amount"] = 12.34m,
                    ["currencyCode"] = "BRL"
                }];
            return Task.FromResult(new TableReportReadResult(
                TableReportReadOutcome.Succeeded,
                new TableReportSnapshot(
                    "transactions", columns, rows, totalCount, 1, criteria.PageSize,
                    [new TableTotalGroupSnapshot(
                        "amount", "BRL", new DateOnly(2026, 9, 8), 12.34m)])));
        }
    }

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult<CurrencySnapshot?>(new CurrencySnapshot(code, code, 2));
    }

    private sealed class StubRateReader : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken) => Task.FromResult<ExchangeRateSnapshot?>(null);
    }

    private sealed class StubRenderer : IDataExportRenderer
    {
        public RenderedDataExport Render(DataExportDocument document, DataExportFormat format) =>
            new([1, 2, 3], "text/csv", "csv");
    }

    private sealed class StubExportStore : IDataExportStore
    {
        public QueueDataExportRequest? Request { get; private set; }

        public Task<QueueDataExportResult> QueueAsync(
            QueueDataExportRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new QueueDataExportResult(ExportId, JobId));
        }

        public Task<DataExportWorkItem?> StartAsync(Guid exportId, DateTimeOffset startedAt,
            CancellationToken cancellationToken) => Task.FromResult<DataExportWorkItem?>(null);

        public Task CompleteAsync(Guid exportId, int rowCount, string contentType,
            string storageKey, DateTimeOffset completedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailAsync(Guid exportId, string reason, DateTimeOffset failedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubQueue : IBackgroundJobQueue
    {
        public Guid? JobId { get; private set; }
        public bool WasEnqueued => JobId.HasValue;
        public int Depth => WasEnqueued ? 1 : 0;

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Guid.Empty);
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActor : IRequestActorAccessor
    {
        public RequestActor? Actor => new(Profile.ExternalSubject!.Value, 3, null, []);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
