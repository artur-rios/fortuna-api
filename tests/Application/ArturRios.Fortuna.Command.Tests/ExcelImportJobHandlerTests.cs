using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
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
        var parser = new StubParser(new InvalidOperationException("broken workbook"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExcelImportJobHandler(store, parser, new FixedTimeProvider(Now))
                .ExecuteAsync(JsonSerializer.Serialize(Payload()), CancellationToken.None));

        Assert.Equal(ExcelImportMessages.WorkbookInvalid, store.FailedReason);
        Assert.Null(store.CompletedJobId);
    }

    private static ExcelImportJobPayload Payload() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        ImportTargetType.Account,
        [1, 2, 3],
        new ExcelColumnMapping("Date", "Amount", "Direction", null, null, null),
        false);

    private sealed class StubParser(Exception? exception = null) : IExcelWorkbookParser
    {
        public IReadOnlyCollection<ExcelWorkbookRow> Rows { get; } =
        [new(2, "{\"Amount\":\"10\"}", new DateOnly(2026, 9, 1), 10m,
            TransactionDirection.Expense, null, null, null, null)];

        public ExcelWorkbookValidation Validate(byte[] content, ExcelColumnMapping mapping) =>
            throw new NotSupportedException();

        public IReadOnlyCollection<ExcelWorkbookRow> Parse(
            byte[] content,
            ExcelColumnMapping mapping)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Rows;
        }
    }

    private sealed class StubStore : IExcelImportStore
    {
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

        public Task CompleteAsync(
            Guid importJobId, Guid userId, Guid targetId, ImportTargetType targetType,
            bool createMissingCategories, IReadOnlyCollection<ExcelWorkbookRow> rows,
            DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            CompletedJobId = importJobId;
            Rows = rows;
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
