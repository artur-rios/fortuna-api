using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ExcelImportJobHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 13, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenQueuedExcelJob_WhenExecuted_ThenRowsAreParsedAndCompleted()
    {
        var store = new StubStore();
        var parser = new StubParser();
        var payload = Payload();

        await new ExcelImportJobHandler(store, parser, new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(payload), CancellationToken.None);

        Assert.Equal(payload.ImportJobId, store.BegunJobId);
        Assert.Equal(payload.ImportJobId, store.CompletedJobId);
        Assert.Same(parser.Rows, store.Rows);
        Assert.Null(store.FailedReason);
    }

    [UnitFact]
    public async Task GivenParserFailure_WhenExecuted_ThenImportJobIsFailed()
    {
        var store = new StubStore();
        var parser = new StubParser(ExcelImportMessages.WorkbookInvalid);

        var result = await new ExcelImportJobHandler(store, parser, new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([ExcelImportMessages.WorkbookInvalid], result.Errors);
        Assert.Equal(ExcelImportMessages.WorkbookInvalid, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    [UnitFact]
    public async Task GivenTargetDeletedWhileRunning_WhenExecuted_ThenJobFailsAsTargetUnavailable()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Of(ImportCompletionOutcome.TargetUnavailable)
        };

        var result = await new ExcelImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([ExcelImportMessages.TargetUnavailable], result.Errors);
        Assert.Equal(ExcelImportMessages.TargetUnavailable, store.FailedReason);
    }

    [UnitFact]
    public async Task GivenJobNoLongerRunning_WhenExecuted_ThenErrorIsReturnedWithoutFailingIt()
    {
        var store = new StubStore
        {
            CompletionResult = ImportCompletionResult.Of(ImportCompletionOutcome.JobNotRunning)
        };

        var result = await new ExcelImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
            .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None);

        Assert.Equal([ImportJobMessages.NoLongerRunning], result.Errors);
        Assert.Null(store.FailedReason);
    }

    [UnitFact]
    public async Task GivenStoreFailure_WhenCompleting_ThenImportJobIsFailedAndFailurePropagates()
    {
        var store = new StubStore { CompleteFailure = new IOException("database unavailable") };

        await Assert.ThrowsAsync<IOException>(() =>
            new ExcelImportJobHandler(store, new StubParser(), new FixedTimeProvider(Now))
                .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None));

        Assert.Equal(ImportJobMessages.ProcessingFailed, store.FailedReason);
    }

    private static ExcelImportJobPayload Payload() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        ImportTargetType.Account,
        [1, 2, 3],
        new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null),
        false);

    private sealed class StubParser(string? error = null) : IExcelWorkbookParser
    {
        public IReadOnlyCollection<ExcelWorkbookRow> Rows { get; } =
        [new(2, "{\"Amount\":\"10\"}", new DateOnly(2026, 9, 1), 10m,
            TransactionDirection.Expense, null, null, null, null)];

        public ExcelWorkbookValidation Validate(byte[] content, ExcelColumnMapping mapping) =>
            throw new NotSupportedException();

        public ExcelWorkbookParseResult Parse(
            byte[] content,
            ExcelColumnMapping mapping) => error is null
            ? ExcelWorkbookParseResult.Success(Rows)
            : ExcelWorkbookParseResult.Failure(error);
    }

    private sealed class StubStore : IExcelImportStore
    {
        public ImportCompletionResult CompletionResult { get; init; } =
            ImportCompletionResult.Completed;
        public Exception? CompleteFailure { get; init; }
        public Guid? BegunJobId { get; private set; }
        public Guid? CompletedJobId { get; private set; }
        public IReadOnlyCollection<ExcelWorkbookRow>? Rows { get; private set; }
        public string? FailedReason { get; private set; }

        public Task<QueueExcelImportResult> QueueAsync(
            ExcelImportRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken)
        {
            BegunJobId = importJobId;

            return Task.FromResult(true);
        }

        public Task<ImportCompletionResult> CompleteAsync(
            Guid importJobId, Guid userId, Guid targetId, ImportTargetType targetType,
            bool createMissingCategories, IReadOnlyCollection<ExcelWorkbookRow> rows,
            DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            if (CompleteFailure is not null)
            {
                return Task.FromException<ImportCompletionResult>(CompleteFailure);
            }

            CompletedJobId = importJobId;
            Rows = rows;

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
