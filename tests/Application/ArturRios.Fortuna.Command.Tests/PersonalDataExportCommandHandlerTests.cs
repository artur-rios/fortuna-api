using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class PersonalDataExportCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Subject = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ExportId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid BackgroundJobId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-10T01:00:00Z");

    [UnitFact]
    public async Task GivenAccountOwner_WhenArchiveRequested_ThenDurableJobIsQueuedWithRetention()
    {
        var exports = new StubExportStore();
        var queue = new StubQueue();
        var handler = new RequestPersonalDataExportCommandHandler(
            new StubActor(new RequestActor(Subject, (int)HeimdallRoles.User, null, [])),
            new StubProfiles(Profile()),
            exports,
            queue,
            new DataExportOptions(1000, TimeSpan.FromHours(24), "pt-BR"),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RequestPersonalDataExportCommand
        {
            CorrelationId = "trace-79"
        });

        Assert.True(result.Success);
        Assert.Equal(ExportId, result.Data!.JobId);
        Assert.Equal(DataExportStatus.Pending, result.Data.Status);
        Assert.Equal(0, result.Data.Progress);
        Assert.Equal(Now.AddHours(24), result.Data.ExpiresAt);
        Assert.Equal(BackgroundJobId, queue.JobId);
        Assert.Equal("trace-79", exports.Queued!.CorrelationId);
        Assert.Equal(UserId, exports.Queued.UserId);
        Assert.Contains(PersonalDataExportMessages.Accepted, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingProfileOrNonOwnerRole_WhenArchiveRequested_ThenNothingIsQueued()
    {
        var missingStore = new StubExportStore();
        var missing = await Handler(
            new RequestActor(Subject, (int)HeimdallRoles.User, null, []),
            null,
            missingStore).HandleAsync(new RequestPersonalDataExportCommand());
        var adminStore = new StubExportStore();
        var admin = await Handler(
            new RequestActor(Subject, (int)HeimdallRoles.SystemAdmin, null, []),
            Profile(),
            adminStore).HandleAsync(new RequestPersonalDataExportCommand());

        Assert.All(new[] { missing, admin }, result =>
        {
            Assert.False(result.Success);
            Assert.Contains(PersonalDataExportMessages.ProfileNotFound, result.Errors);
        });
        Assert.Null(missingStore.Queued);
        Assert.Null(adminStore.Queued);
    }

    [UnitFact]
    public async Task GivenPersonalArchiveJob_WhenProcessed_ThenZipIsStoredAndExportCompletes()
    {
        var exports = new StubExportStore
        {
            Work = new PersonalDataExportWorkItem(
                ExportId, UserId, "personal.zip", Now.AddHours(24))
        };
        var storage = new MemoryStorage();
        var handler = new PersonalDataExportJobHandler(
            exports,
            exports,
            new StubArchiveBuilder(new PersonalDataArchive([1, 2, 3], 7, ["profile"])),
            storage,
            new FixedTimeProvider(Now));

        await handler.ExecuteAsync(
            JsonSerializer.Serialize(new PersonalDataExportJobPayload(ExportId)),
            CancellationToken.None);

        Assert.Equal(ExportId, exports.CompletedId);
        Assert.Equal(7, exports.CompletedCount);
        Assert.Equal("application/zip", exports.CompletedContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, storage.Content);
        Assert.Null(exports.FailedId);
    }

    [UnitFact]
    public async Task GivenArchiveBuildFailure_WhenJobProcessed_ThenFailureIsPersistedAndRethrown()
    {
        var exports = new StubExportStore
        {
            Work = new PersonalDataExportWorkItem(
                ExportId, UserId, "personal.zip", Now.AddHours(24))
        };
        var handler = new PersonalDataExportJobHandler(
            exports,
            exports,
            new StubArchiveBuilder(new IOException("archive failed")),
            new MemoryStorage(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<IOException>(() => handler.ExecuteAsync(
            JsonSerializer.Serialize(new PersonalDataExportJobPayload(ExportId)),
            CancellationToken.None));

        Assert.Equal(ExportId, exports.FailedId);
        Assert.Equal(PersonalDataExportMessages.GenerationFailed, exports.FailureReason);
        Assert.Null(exports.CompletedId);
    }

    private static RequestPersonalDataExportCommandHandler Handler(
        RequestActor actor,
        UserProfileSnapshot? profile,
        StubExportStore exports) => new(
        new StubActor(actor),
        new StubProfiles(profile),
        exports,
        new StubQueue(),
        new DataExportOptions(1000, TimeSpan.FromHours(24), "pt-BR"),
        new FixedTimeProvider(Now));

    private static UserProfileSnapshot Profile() => new(
        UserId, Subject, "Portable", "BRL", false, Now, Now);

    private sealed class StubActor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor Actor => actor;
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubQueue : IBackgroundJobQueue
    {
        public Guid? JobId { get; private set; }
        public int Depth => JobId.HasValue ? 1 : 0;

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(JobId ?? Guid.Empty);
    }

    private sealed class StubExportStore : IPersonalDataExportStore, IDataExportStore
    {
        public QueuePersonalDataExportRequest? Queued { get; private set; }
        public PersonalDataExportWorkItem? Work { get; init; }
        public Guid? CompletedId { get; private set; }
        public int? CompletedCount { get; private set; }
        public string? CompletedContentType { get; private set; }
        public Guid? FailedId { get; private set; }
        public string? FailureReason { get; private set; }

        public Task<QueueDataExportResult> QueuePersonalAsync(
            QueuePersonalDataExportRequest request,
            CancellationToken cancellationToken)
        {
            Queued = request;
            return Task.FromResult(new QueueDataExportResult(ExportId, BackgroundJobId));
        }

        public Task<PersonalDataExportWorkItem?> StartPersonalAsync(
            Guid exportId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => Task.FromResult(Work);

        public Task<DataExportReadSnapshot?> FindOwnedPersonalAsync(
            Guid userId,
            Guid exportId,
            CancellationToken cancellationToken) => Task.FromResult<DataExportReadSnapshot?>(null);

        public Task<QueueDataExportResult> QueueAsync(
            QueueDataExportRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DataExportWorkItem?> StartAsync(
            Guid exportId,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(
            Guid exportId,
            int rowCount,
            string contentType,
            string storageKey,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            CompletedId = exportId;
            CompletedCount = rowCount;
            CompletedContentType = contentType;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid exportId,
            string reason,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            FailedId = exportId;
            FailureReason = reason;
            return Task.CompletedTask;
        }
    }

    private sealed class StubArchiveBuilder : IPersonalDataArchiveBuilder
    {
        private readonly PersonalDataArchive? archive;
        private readonly Exception? exception;

        public StubArchiveBuilder(PersonalDataArchive archive) => this.archive = archive;
        public StubArchiveBuilder(Exception exception) => this.exception = exception;

        public Task<PersonalDataArchive> BuildAsync(
            Guid userId,
            DateTimeOffset generatedAt,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken) => exception is null
            ? Task.FromResult(archive!)
            : Task.FromException<PersonalDataArchive>(exception);
    }

    private sealed class MemoryStorage : IAttachmentStore
    {
        public byte[]? Content { get; private set; }

        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Content = copy.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
