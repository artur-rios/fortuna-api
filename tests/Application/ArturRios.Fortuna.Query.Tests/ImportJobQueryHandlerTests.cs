using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ImportJobQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 21, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenCompletedOwnedJob_WhenRead_ThenProgressAndCountsAreReturned()
    {
        var user = User();
        var job = Job(user);
        job.Start(Now.AddMinutes(1));
        job.SetPeriod(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), Now);
        job.Complete(7, 2, 1, Now.AddMinutes(2));

        var result = await GetHandler(Profile(user), new StubReader(job)).HandleAsync(
            new GetImportJobByIdQuery { Id = job.PublicId });

        Assert.True(result.Success);
        Assert.Equal(job.PublicId, result.Data?.Id);
        Assert.Equal(ImportJobStatus.Completed, result.Data?.Status);
        Assert.Equal(10, result.Data?.ProcessedCount);
        Assert.Equal((7, 2, 1), (result.Data!.ImportedCount,
            result.Data.DuplicateCount, result.Data.RejectedCount));
        Assert.Equal(job.PeriodStart, result.Data.PeriodStart);
    }

    [UnitFact]
    public async Task GivenRunningJob_WhenRead_ThenCurrentStateReturnsWithoutWaiting()
    {
        var user = User();
        var job = Job(user);
        job.Start(Now.AddMinutes(1));

        var result = await GetHandler(Profile(user), new StubReader(job)).HandleAsync(
            new GetImportJobByIdQuery { Id = job.PublicId });

        Assert.True(result.Success);
        Assert.Equal(ImportJobStatus.Running, result.Data?.Status);
        Assert.Equal(0, result.Data?.ProcessedCount);
    }

    [UnitFact]
    public async Task GivenFailedJob_WhenRead_ThenApplicationFailureReasonIsReturned()
    {
        var user = User();
        var job = Job(user);
        job.Start(Now);
        job.Fail(PdfInvoiceImportMessages.UnsupportedLayout, Now.AddMinutes(1));

        var result = await GetHandler(Profile(user), new StubReader(job)).HandleAsync(
            new GetImportJobByIdQuery { Id = job.PublicId });

        Assert.True(result.Success);
        Assert.Equal(ImportJobStatus.Failed, result.Data?.Status);
        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, result.Data?.FailureReason);
    }

    [UnitFact]
    public async Task GivenForeignOrMissingJob_WhenRead_ThenSameNotFoundIsReturned()
    {
        var owner = User();
        var actor = User();
        var job = Job(owner);
        var handler = GetHandler(Profile(actor), new StubReader(job));

        var foreign = await handler.HandleAsync(new GetImportJobByIdQuery { Id = job.PublicId });
        var missing = await handler.HandleAsync(new GetImportJobByIdQuery { Id = Guid.NewGuid() });

        Assert.Equal(foreign.Errors, missing.Errors);
        Assert.Contains(ImportJobMessages.NotFound, foreign.Errors);
    }

    [UnitFact]
    public async Task GivenStatusFilter_WhenListed_ThenOnlyOwnedMatchesAreReturned()
    {
        var actor = User();
        var completed = Job(actor);
        completed.Start(Now);
        completed.Complete(1, 0, 0, Now);
        var pending = Job(actor);
        var foreign = Job(User());
        foreign.Start(Now);
        foreign.Complete(1, 0, 0, Now);
        var handler = ListHandler(Profile(actor), new StubReader(completed, pending, foreign));

        var result = await handler.HandleAsync(new ListImportJobsQuery
        {
            Status = ImportJobStatus.Completed,
            SortBy = "UpdatedAt"
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalItems);
        Assert.Equal(completed.PublicId, result.Data?.Single().Id);
    }

    [UnitFact]
    public async Task GivenMixedRowOutcomes_WhenRecordsListed_ThenPayloadsAndReasonsAreReturned()
    {
        var user = User();
        var job = Job(user);
        var imported = Record(job, "{\"row\":1}", ImportedRecordOutcome.Imported);
        var rejected = Record(
            job,
            "{\"row\":2}",
            ImportedRecordOutcome.Rejected,
            ExcelImportMessages.RowDateInvalid);
        var reader = new StubReader([job], [imported, rejected]);

        var result = await RecordsHandler(Profile(user), reader).HandleAsync(
            new ListImportedRecordsQuery { ImportJobId = job.PublicId });

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalItems);
        Assert.Contains(result.Data!, item => item.Outcome == ImportedRecordOutcome.Imported &&
            item.RawPayload == "{\"row\":1}" && item.TransactionId == null &&
            !item.HasLiveTransaction);
        Assert.Contains(result.Data!, item => item.Outcome == ImportedRecordOutcome.Rejected &&
            item.RejectionReason == ExcelImportMessages.RowDateInvalid);
    }

    [UnitFact]
    public async Task GivenForeignJob_WhenRecordsListed_ThenItIsNotFound()
    {
        var owner = User();
        var actor = User();
        var job = Job(owner);

        var result = await RecordsHandler(Profile(actor), new StubReader(job)).HandleAsync(
            new ListImportedRecordsQuery { ImportJobId = job.PublicId });

        Assert.False(result.Success);
        Assert.Contains(ImportJobMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenNamedErrorsAreReturned()
    {
        var result = await ListHandler(null, new StubReader()).HandleAsync(
            new ListImportJobsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                SourceType = (TransactionSourceType)99,
                Status = (ImportJobStatus)99,
                SortBy = "Name"
            });

        Assert.Contains(ImportJobMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(ImportJobMessages.InvalidPageSize, result.Errors);
        Assert.Contains(ImportJobMessages.SourceTypeInvalid, result.Errors);
        Assert.Contains(ImportJobMessages.StatusInvalid, result.Errors);
        Assert.Contains(ImportJobMessages.SortByUnsupported, result.Errors);
    }

    private static GetImportJobByIdQueryHandler GetHandler(
        UserProfileSnapshot? profile,
        IImportJobReader reader) => new(new StubProfileReader(profile), reader, Actor(profile));

    private static ListImportJobsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        IImportJobReader reader) => new(
        new ListImportJobsQueryValidator(),
        new StubProfileReader(profile),
        reader,
        Actor(profile),
        new PaginationOptions(100));

    private static ListImportedRecordsQueryHandler RecordsHandler(
        UserProfileSnapshot? profile,
        IImportJobReader reader) => new(
        new ListImportedRecordsQueryValidator(),
        new StubProfileReader(profile),
        reader,
        Actor(profile),
        new PaginationOptions(100));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfile User() => new(
        Guid.NewGuid(), "Owner", new Currency("BRL", "Brazilian real", 2), Now);

    private static ImportJob Job(UserProfile user) =>
        new(user, TransactionSourceType.Excel, Now);

    private static ImportedRecord Record(
        ImportJob job,
        string payload,
        ImportedRecordOutcome outcome,
        string? reason = null) => new(
        job,
        payload,
        outcome,
        10m,
        new DateOnly(2026, 9, 1),
        rejectionReason: reason);

    private static UserProfileSnapshot Profile(UserProfile user) => new(
        user.PublicId,
        Guid.Parse(user.ExternalSubject!),
        user.DisplayName,
        user.DisplayCurrency.Code,
        false,
        user.CreatedAt,
        user.UpdatedAt);

    private sealed class StubReader : IImportJobReader
    {
        private readonly ImportJob[] jobs;
        private readonly ImportedRecord[] records;

        public StubReader(params ImportJob[] jobs) : this(jobs, [])
        {
        }

        public StubReader(ImportJob[] jobs, ImportedRecord[] records)
        {
            this.jobs = jobs;
            this.records = records;
        }

        public IQueryable<ImportJob> Query() => jobs.AsQueryable();
        public IQueryable<ImportedRecord> Records() => records.AsQueryable();

        public Task<ImportJobReadSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken)
        {
            var job = jobs.SingleOrDefault(item =>
                item.User.PublicId == userId && item.PublicId == id);
            return Task.FromResult(job is null ? null : Snapshot(job));
        }

        public Task<bool> IsOwnedAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) => Task.FromResult(jobs.Any(item =>
            item.User.PublicId == userId && item.PublicId == id));

        private static ImportJobReadSnapshot Snapshot(ImportJob job) => new(
            job.PublicId,
            job.Connection?.PublicId,
            job.SourceType,
            job.Status,
            job.PeriodStart,
            job.PeriodEnd,
            job.ImportedCount,
            job.DuplicateCount,
            job.RejectedCount,
            job.FailureReason,
            job.CreatedAt,
            job.UpdatedAt);
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
}
