using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ImportExcelWorkbookCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidWorkbook_WhenImported_ThenDurableJobIsQueued()
    {
        var store = new StubStore(QueueExcelImportOutcome.Succeeded);
        var queue = new StubQueue();
        var command = Command();

        var result = await Handler(store, queue, new StubParser(true)).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(store.JobId, result.Data?.ImportJobId);
        Assert.Equal(ImportJobStatus.Pending, result.Data?.Status);
        Assert.Equal(store.BackgroundJobId, queue.JobId);
        Assert.Equal(command.TargetId, store.Request?.TargetId);
        Assert.Equal(command.Content, store.Request?.Content);
    }

    [UnitFact]
    public async Task GivenMissingRequiredMapping_WhenImported_ThenNothingIsQueued()
    {
        var store = new StubStore(QueueExcelImportOutcome.Succeeded);
        var command = Command();
        command.Mapping = command.Mapping with { Amount = "" };

        var result = await Handler(store, new StubQueue(), new StubParser(true))
            .HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(ExcelImportMessages.AmountColumnRequired, result.Errors);
        Assert.Null(store.Request);
    }

    [UnitFact]
    public async Task GivenUnreadableWorkbook_WhenImported_ThenNothingIsQueued()
    {
        var store = new StubStore(QueueExcelImportOutcome.Succeeded);

        var result = await Handler(store, new StubQueue(), new StubParser(false))
            .HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(ExcelImportMessages.WorkbookInvalid, result.Errors);
        Assert.Null(store.Request);
    }

    [UnitTheory]
    [InlineData(QueueExcelImportOutcome.TargetNotFound, ExcelImportMessages.TargetNotFound)]
    [InlineData(QueueExcelImportOutcome.TargetDeleted, ExcelImportMessages.TargetDeleted)]
    public async Task GivenUnavailableTarget_WhenImported_ThenExpectedErrorIsReturned(
        QueueExcelImportOutcome outcome,
        string expected)
    {
        var result = await Handler(
            new StubStore(outcome),
            new StubQueue(),
            new StubParser(true)).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenOversizedWorkbook_WhenImported_ThenFileIsRejectedBeforeParsing()
    {
        var parser = new StubParser(true);
        var command = Command();
        command.Content = new byte[101];

        var result = await Handler(
            new StubStore(QueueExcelImportOutcome.Succeeded),
            new StubQueue(),
            parser,
            maximumBytes: 100).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(ExcelImportMessages.FileTooLarge, result.Errors);
        Assert.Equal(0, parser.ValidationCount);
    }

    private static ImportExcelWorkbookCommandHandler Handler(
        StubStore store,
        StubQueue queue,
        StubParser parser,
        int maximumBytes = 1024) => new(
        new ImportExcelWorkbookCommandValidator(new ExcelImportOptions(maximumBytes)),
        new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(new UserProfileSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now)),
        parser,
        store,
        queue,
        new FixedTimeProvider(Now));

    private static ImportExcelWorkbookCommand Command() => new()
    {
        TargetId = Guid.NewGuid(),
        TargetType = ImportTargetType.Account,
        FileName = "transactions.xlsx",
        Content = [1, 2, 3],
        Mapping = new ExcelColumnMapping(
            "Date", "Amount", "Direction", "Description", "Category", "Id"),
        CreateMissingCategories = true,
        CorrelationId = "request-42"
    };

    private sealed class StubStore(QueueExcelImportOutcome outcome) : IExcelImportStore
    {
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid BackgroundJobId { get; } = Guid.NewGuid();
        public ExcelImportRequest? Request { get; private set; }

        public Task<QueueExcelImportResult> QueueAsync(
            ExcelImportRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var job = outcome == QueueExcelImportOutcome.Succeeded
                ? new ExcelImportJobSnapshot(JobId, ImportJobStatus.Pending, Now, Now)
                : null;
            return Task.FromResult(new QueueExcelImportResult(
                job,
                job is null ? null : BackgroundJobId,
                outcome));
        }

        public Task<bool> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CompleteAsync(
            Guid importJobId, Guid userId, Guid targetId, ImportTargetType targetType,
            bool createMissingCategories, IReadOnlyCollection<ExcelWorkbookRow> rows,
            DateTimeOffset completedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            Guid importJobId, string reason, DateTimeOffset failedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubParser(bool valid) : IExcelWorkbookParser
    {
        public int ValidationCount { get; private set; }

        public ExcelWorkbookValidation Validate(byte[] content, ExcelColumnMapping mapping)
        {
            ValidationCount++;
            return new ExcelWorkbookValidation(
                valid,
                valid ? null : ExcelImportMessages.WorkbookInvalid);
        }

        public IReadOnlyCollection<ExcelWorkbookRow> Parse(
            byte[] content, ExcelColumnMapping mapping) => throw new NotSupportedException();
    }

    private sealed class StubQueue : IBackgroundJobQueue
    {
        public int Depth => JobId.HasValue ? 1 : 0;
        public Guid? JobId { get; private set; }

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
