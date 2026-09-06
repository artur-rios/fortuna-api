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

public sealed class ImportPdfInvoiceCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidPdfUpload_WhenImported_ThenDurableJobIsQueued()
    {
        var store = new StubStore(QueuePdfInvoiceImportOutcome.Succeeded);
        var queue = new StubQueue();
        var command = Command();

        var result = await Handler(store, queue).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(store.JobId, result.Data?.ImportJobId);
        Assert.Equal(ImportJobStatus.Pending, result.Data?.Status);
        Assert.Equal(store.BackgroundJobId, queue.JobId);
        Assert.Equal(command.CreditCardId, store.Request?.CreditCardId);
        Assert.Equal(command.Content, store.Request?.Content);
    }

    [UnitTheory]
    [InlineData(QueuePdfInvoiceImportOutcome.CreditCardNotFound,
        PdfInvoiceImportMessages.CreditCardNotFound)]
    [InlineData(QueuePdfInvoiceImportOutcome.CreditCardDeleted,
        PdfInvoiceImportMessages.CreditCardDeleted)]
    public async Task GivenUnavailableCard_WhenImported_ThenExpectedErrorIsReturned(
        QueuePdfInvoiceImportOutcome outcome,
        string expected)
    {
        var result = await Handler(new StubStore(outcome), new StubQueue())
            .HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenOversizedPdf_WhenImported_ThenNothingIsQueued()
    {
        var store = new StubStore(QueuePdfInvoiceImportOutcome.Succeeded);
        var command = Command();
        command.Content = new byte[101];

        var result = await Handler(store, new StubQueue(), 100).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(PdfInvoiceImportMessages.FileTooLarge, result.Errors);
        Assert.Null(store.Request);
    }

    [UnitFact]
    public async Task GivenMissingPdf_WhenImported_ThenNothingIsQueued()
    {
        var store = new StubStore(QueuePdfInvoiceImportOutcome.Succeeded);
        var command = Command();
        command.Content = [];

        var result = await Handler(store, new StubQueue()).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(PdfInvoiceImportMessages.FileRequired, result.Errors);
        Assert.Null(store.Request);
    }

    private static ImportPdfInvoiceCommandHandler Handler(
        StubStore store,
        StubQueue queue,
        int maximumBytes = 1024) => new(
        new ImportPdfInvoiceCommandValidator(new PdfInvoiceImportOptions(maximumBytes)),
        new StubActorAccessor(new RequestActor(Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(new UserProfileSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now)),
        store,
        queue,
        new FixedTimeProvider(Now));

    private static ImportPdfInvoiceCommand Command() => new()
    {
        CreditCardId = Guid.NewGuid(),
        FileName = "invoice.pdf",
        Content = [1, 2, 3],
        CorrelationId = "request-60"
    };

    private sealed class StubStore(QueuePdfInvoiceImportOutcome outcome) : IPdfInvoiceImportStore
    {
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid BackgroundJobId { get; } = Guid.NewGuid();
        public PdfInvoiceImportRequest? Request { get; private set; }

        public Task<QueuePdfInvoiceImportResult> QueueAsync(
            PdfInvoiceImportRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var job = outcome == QueuePdfInvoiceImportOutcome.Succeeded
                ? new PdfInvoiceImportJobSnapshot(JobId, ImportJobStatus.Pending, Now, Now)
                : null;
            return Task.FromResult(new QueuePdfInvoiceImportResult(
                job,
                job is null ? null : BackgroundJobId,
                outcome));
        }

        public Task<bool> BeginAsync(
            Guid importJobId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CompleteAsync(
            Guid importJobId, Guid userId, Guid creditCardId, ParsedPdfInvoice invoice,
            DateTimeOffset completedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            Guid importJobId, string reason, DateTimeOffset failedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
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
