using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class DownloadAttachmentQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AttachmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [UnitFact]
    public async Task GivenOwnedLiveAttachment_WhenDownloaded_ThenStoredContentAndMetadataAreReturned()
    {
        var content = new MemoryStream([1, 2, 3]);
        var storage = Storage(healthy: true);
        storage.Setup(item => item.OpenReadAsync("attachments/key", CancellationToken.None))
            .ReturnsAsync(content);

        var result = await Handler(Metadata(), storage).HandleAsync(Query());

        Assert.True(result.Success);
        Assert.Equal(AttachmentId, result.Data?.Id);
        Assert.Equal("receipt.pdf", result.Data?.FileName);
        Assert.Equal("application/pdf", result.Data?.ContentType);
        Assert.Equal(3, result.Data?.SizeInBytes);
        Assert.Same(content, result.Data?.Content);
    }

    [UnitFact]
    public async Task GivenForeignDeletedOrMissingAttachment_WhenDownloaded_ThenItIsHidden()
    {
        var storage = Storage(healthy: true);

        var result = await Handler(Metadata(null, returnDefault: true), storage).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.AttachmentNotFound, result.Errors);
        storage.Verify(item => item.IsHealthyAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitFact]
    public async Task GivenMissingStoredObject_WhenDownloaded_ThenNotFoundAndDiscrepancyAuditAreReturned()
    {
        var storage = Storage(healthy: true);
        storage.Setup(item => item.OpenReadAsync("attachments/key", CancellationToken.None))
            .ThrowsAsync(new AttachmentObjectNotFoundException("attachments/key"));
        var audit = new Mock<IAuditEntryWriter>();

        var result = await Handler(Metadata(), storage, audit).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.StoredObjectNotFound, result.Errors);
        audit.Verify(writer => writer.WriteAsync(
            nameof(DownloadAttachmentQuery),
            "Attachment",
            AttachmentId,
            false,
            AttachmentMessages.StoredObjectNotFound), Times.Once);
    }

    [UnitFact]
    public async Task GivenUnreachableStorage_WhenDownloaded_ThenOnlyThisRequestIsUnavailable()
    {
        var storage = Storage(healthy: false);

        var result = await Handler(Metadata(), storage).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.StorageUnavailable, result.Errors);
        storage.Verify(item => item.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [UnitFact]
    public async Task GivenStorageReadFailure_WhenDownloaded_ThenServiceUnavailableIsReturned()
    {
        var storage = Storage(healthy: true);
        storage.Setup(item => item.OpenReadAsync("attachments/key", CancellationToken.None))
            .ThrowsAsync(new IOException("offline"));

        var result = await Handler(Metadata(), storage).HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.StorageUnavailable, result.Errors);
    }

    [UnitFact]
    public async Task GivenEmptyIdentifier_WhenDownloaded_ThenValidationStopsTheRead()
    {
        var metadata = Metadata();

        var result = await Handler(metadata, Storage(healthy: true))
            .HandleAsync(new DownloadAttachmentQuery());

        Assert.Contains(AttachmentMessages.AttachmentNotFound, result.Errors);
        metadata.Verify(reader => reader.FindOwnedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DownloadAttachmentQueryHandler Handler(
        Mock<IAttachmentMetadataReader> metadata,
        Mock<IAttachmentStore> storage,
        Mock<IAuditEntryWriter>? audit = null) => new(
        new DownloadAttachmentQueryValidator(),
        new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
        new StubProfileReader(new UserProfileSnapshot(
            UserId, null, "Owner", "BRL", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)),
        metadata.Object,
        storage.Object,
        (audit ?? new Mock<IAuditEntryWriter>()).Object,
        NullLogger<DownloadAttachmentQueryHandler>.Instance);

    private static Mock<IAttachmentMetadataReader> Metadata(
        AttachmentReadSnapshot? snapshot = null,
        bool returnDefault = false)
    {
        var metadata = new Mock<IAttachmentMetadataReader>();
        metadata.Setup(reader => reader.FindOwnedAsync(UserId, AttachmentId, CancellationToken.None))
            .ReturnsAsync(returnDefault
                ? null
                : snapshot ?? new AttachmentReadSnapshot(
                    AttachmentId,
                    "receipt.pdf",
                    "application/pdf",
                    3,
                    "attachments/key"));
        return metadata;
    }

    private static Mock<IAttachmentStore> Storage(bool healthy)
    {
        var storage = new Mock<IAttachmentStore>();
        storage.Setup(item => item.IsHealthyAsync(CancellationToken.None)).ReturnsAsync(healthy);
        return storage;
    }

    private static DownloadAttachmentQuery Query() => new() { Id = AttachmentId };

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
