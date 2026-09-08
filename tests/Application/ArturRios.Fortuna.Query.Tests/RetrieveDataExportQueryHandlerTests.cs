using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class RetrieveDataExportQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExportId = Guid.Parse(
        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(
        2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenCompletedOwnedExport_WhenRetrieved_ThenStoredFileIsReturned()
    {
        var content = new MemoryStream([1, 2, 3]);
        var storage = Storage(healthy: true);
        storage.Setup(item => item.OpenReadAsync("exports/file.csv", CancellationToken.None))
            .ReturnsAsync(content);

        var result = await Handler(Reader(Snapshot(DataExportStatus.Completed)), storage)
            .HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(ExportId, result.Data?.Id);
        Assert.Equal(DataExportStatus.Completed, result.Data?.Status);
        Assert.Equal(2, result.Data?.RowCount);
        Assert.Equal("export.csv", result.Data?.FileName);
        Assert.Equal("text/csv", result.Data?.ContentType);
        Assert.Same(content, result.Data?.Content);
    }

    [UnitTheory]
    [InlineData(DataExportStatus.Pending)]
    [InlineData(DataExportStatus.Running)]
    public async Task GivenUnfinishedExport_WhenRetrieved_ThenStateReturnsWithoutWaiting(
        DataExportStatus status)
    {
        var storage = Storage(healthy: true);

        var result = await Handler(Reader(Snapshot(status)), storage).HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(status, result.Data?.Status);
        Assert.Null(result.Data?.Content);
        storage.Verify(item => item.IsHealthyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitFact]
    public async Task GivenFailedExport_WhenRetrieved_ThenSystemFailureReasonIsReturned()
    {
        var result = await Handler(
            Reader(Snapshot(
                DataExportStatus.Failed,
                failureReason: DataExportMessages.GenerationFailed)),
            Storage(healthy: true)).HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(DataExportStatus.Failed, result.Data?.Status);
        Assert.Equal(DataExportMessages.GenerationFailed, result.Data?.FailureReason);
        Assert.Null(result.Data?.Content);
    }

    [UnitFact]
    public async Task GivenForeignOrMissingExport_WhenRetrieved_ThenIdentifierIsHidden()
    {
        var reader = Reader(null);

        var result = await Handler(reader, Storage(healthy: true)).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(DataExportMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenEmptyIdentifier_WhenRetrieved_ThenValidationStopsTheRead()
    {
        var reader = Reader(Snapshot(DataExportStatus.Completed));

        var result = await Handler(reader, Storage(healthy: true))
            .HandleAsync(new GetDataExportQuery());

        Assert.False(result.Success);
        Assert.Contains(DataExportMessages.NotFound, result.Errors);
        reader.Verify(item => item.FindOwnedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitFact]
    public async Task GivenExpiredCompletedExport_WhenRetrieved_ThenNewExportIsRequested()
    {
        var storage = Storage(healthy: true);

        var result = await Handler(
            Reader(Snapshot(DataExportStatus.Completed, expiresAt: Now.AddSeconds(-1))),
            storage).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(DataExportMessages.Expired, result.Errors);
        storage.Verify(item => item.OpenReadAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitFact]
    public async Task GivenMissingStoredFile_WhenRetrieved_ThenNewExportIsRequested()
    {
        var storage = Storage(healthy: true);
        storage.Setup(item => item.OpenReadAsync("exports/file.csv", CancellationToken.None))
            .ThrowsAsync(new AttachmentObjectNotFoundException("exports/file.csv"));

        var result = await Handler(Reader(Snapshot(DataExportStatus.Completed)), storage)
            .HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(DataExportMessages.FileNotFound, result.Errors);
    }

    private static RetrieveDataExportQueryHandler Handler(
        Mock<IDataExportReader> reader,
        Mock<IAttachmentStore> storage) => new(
        new GetDataExportQueryValidator(),
        new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
        new StubProfileReader(new UserProfileSnapshot(
            UserId, null, "Owner", "BRL", false, Now, Now)),
        reader.Object,
        storage.Object,
        new FixedTimeProvider(),
        NullLogger<RetrieveDataExportQueryHandler>.Instance);

    private static Mock<IDataExportReader> Reader(DataExportReadSnapshot? snapshot)
    {
        var reader = new Mock<IDataExportReader>();
        reader.Setup(item => item.FindOwnedAsync(
                UserId,
                ExportId,
                CancellationToken.None))
            .ReturnsAsync(snapshot);
        return reader;
    }

    private static Mock<IAttachmentStore> Storage(bool healthy)
    {
        var storage = new Mock<IAttachmentStore>();
        storage.Setup(item => item.IsHealthyAsync(CancellationToken.None))
            .ReturnsAsync(healthy);
        return storage;
    }

    private static DataExportReadSnapshot Snapshot(
        DataExportStatus status,
        string? failureReason = null,
        DateTimeOffset? expiresAt = null) => new(
        ExportId,
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        DataExportFormat.Csv,
        status,
        "export.csv",
        status == DataExportStatus.Completed ? 2 : null,
        status == DataExportStatus.Completed ? "text/csv" : null,
        status == DataExportStatus.Completed ? "exports/file.csv" : null,
        failureReason,
        Now.AddMinutes(-2),
        Now.AddMinutes(-1),
        expiresAt ?? Now.AddHours(1));

    private static GetDataExportQuery Query() => new() { Id = ExportId };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
