using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class GetPersonalDataExportQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Subject = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid JobId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T01:00:00Z");

    [UnitTheory]
    [InlineData(DataExportStatus.Pending, 0)]
    [InlineData(DataExportStatus.Running, 50)]
    public async Task GivenUnfinishedArchive_WhenStatusRead_ThenProgressIsReported(
        DataExportStatus status,
        int progress)
    {
        var result = await Handler(Snapshot(status), new StubStorage())
            .HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(status, result.Data!.Status);
        Assert.Equal(progress, result.Data.Progress);
        Assert.Null(result.Data.Content);
    }

    [UnitFact]
    public async Task GivenFailedArchive_WhenStatusRead_ThenSanitizedFailureIsReported()
    {
        var result = await Handler(Snapshot(
            DataExportStatus.Failed,
            failure: PersonalDataExportMessages.GenerationFailed), new StubStorage())
            .HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(100, result.Data!.Progress);
        Assert.Equal(PersonalDataExportMessages.GenerationFailed, result.Data.FailureReason);
    }

    [UnitFact]
    public async Task GivenCompletedArchive_WhenRead_ThenStoredZipIsReturned()
    {
        var storage = new StubStorage { Content = [1, 2, 3] };
        var result = await Handler(Snapshot(DataExportStatus.Completed), storage)
            .HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal("application/zip", result.Data!.ContentType);
        Assert.NotNull(result.Data.Content);
    }

    [UnitFact]
    public async Task GivenForeignMissingOrExpiredArchive_WhenRead_ThenIdentifierIsHiddenOrGone()
    {
        var missing = await Handler(null, new StubStorage()).HandleAsync(Query());
        var expired = await Handler(
            Snapshot(DataExportStatus.Completed, expiresAt: Now),
            new StubStorage()).HandleAsync(Query());

        Assert.False(missing.Success);
        Assert.Contains(PersonalDataExportMessages.NotFound, missing.Errors);
        Assert.False(expired.Success);
        Assert.Contains(PersonalDataExportMessages.Expired, expired.Errors);
    }

    [UnitFact]
    public async Task GivenMissingOrUnavailableStoredArchive_WhenRead_ThenSafeErrorIsReturned()
    {
        var missing = await Handler(
            Snapshot(DataExportStatus.Completed),
            new StubStorage { Missing = true }).HandleAsync(Query());
        var unavailable = await Handler(
            Snapshot(DataExportStatus.Completed),
            new StubStorage { Healthy = false }).HandleAsync(Query());

        Assert.False(missing.Success);
        Assert.Contains(PersonalDataExportMessages.FileNotFound, missing.Errors);
        Assert.False(unavailable.Success);
        Assert.Contains(PersonalDataExportMessages.StorageUnavailable, unavailable.Errors);
    }

    private static GetPersonalDataExportQueryHandler Handler(
        DataExportReadSnapshot? snapshot,
        StubStorage storage) => new(
        new StubActor(),
        new StubProfiles(),
        new StubExports(snapshot),
        storage,
        new FixedTimeProvider(Now),
        NullLogger<GetPersonalDataExportQueryHandler>.Instance);

    private static GetPersonalDataExportQuery Query() => new() { JobId = JobId };

    private static DataExportReadSnapshot Snapshot(
        DataExportStatus status,
        string? failure = null,
        DateTimeOffset? expiresAt = null) => new(
        JobId,
        Guid.NewGuid(),
        DataExportFormat.Zip,
        status,
        "personal.zip",
        status == DataExportStatus.Completed ? 2 : null,
        status == DataExportStatus.Completed ? "application/zip" : null,
        status == DataExportStatus.Completed ? "exports/personal.zip" : null,
        failure,
        Now.AddMinutes(-2),
        Now.AddMinutes(-1),
        expiresAt ?? Now.AddHours(24));

    private sealed class StubActor : IRequestActorAccessor
    {
        public RequestActor Actor => new(Subject, 1, null, []);
    }

    private sealed class StubProfiles : IUserProfileReader
    {
        private static readonly UserProfileSnapshot Profile = new(
            UserId, Subject, "Portable", "BRL", false, Now, Now);

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult<UserProfileSnapshot?>(Profile);
        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult<UserProfileSnapshot?>(Profile);
    }

    private sealed class StubExports(DataExportReadSnapshot? snapshot) : IPersonalDataExportStore
    {
        public Task<DataExportReadSnapshot?> FindOwnedPersonalAsync(
            Guid userId,
            Guid exportId,
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
        public Task<QueueDataExportResult> QueuePersonalAsync(
            QueuePersonalDataExportRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PersonalDataExportWorkItem?> StartPersonalAsync(
            Guid exportId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubStorage : IAttachmentStore
    {
        public byte[] Content { get; init; } = [1];
        public bool Missing { get; init; }
        public bool Healthy { get; init; } = true;

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Healthy);
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            if (Missing)
            {
                throw new AttachmentObjectNotFoundException(key);
            }
            return Task.FromResult<Stream>(new MemoryStream(Content, writable: false));
        }
        public Task WriteAsync(string key, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
