using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class AttachDocumentCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 23, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TransactionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [UnitFact]
    public async Task GivenValidOwnedTransaction_WhenDocumentAttached_ThenObjectAndMetadataAreSaved()
    {
        var metadata = new StubMetadataStore();
        var storage = new StubAttachmentStore();

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.True(result.Success);
        Assert.Equal(TransactionId, result.Data?.TransactionId);
        Assert.Equal("receipt.pdf", result.Data?.FileName);
        Assert.Equal("application/pdf", result.Data?.ContentType);
        Assert.Equal(3, result.Data?.SizeInBytes);
        Assert.Equal([1, 2, 3], storage.WrittenContent);
        Assert.Equal(storage.WrittenKey, metadata.Write?.StorageKey);
        Assert.Null(storage.DeletedKey);
    }

    [UnitFact]
    public async Task GivenForeignOrDeletedTransaction_WhenDocumentAttached_ThenItIsHiddenAndNothingIsStored()
    {
        var metadata = new StubMetadataStore { IsOwned = false };
        var storage = new StubAttachmentStore();

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.TransactionNotFound, result.Errors);
        Assert.Null(storage.WrittenKey);
        Assert.Null(metadata.Write);
    }

    [UnitFact]
    public async Task GivenUnavailableStorage_WhenDocumentAttached_ThenServiceUnavailableIsReturnedWithoutMetadata()
    {
        var metadata = new StubMetadataStore();
        var storage = new StubAttachmentStore { Healthy = false };

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.StorageUnavailable, result.Errors);
        Assert.Null(storage.WrittenKey);
        Assert.Null(metadata.Write);
    }

    [UnitFact]
    public async Task GivenStorageWriteFailure_WhenDocumentAttached_ThenPartialObjectIsCompensated()
    {
        var metadata = new StubMetadataStore();
        var storage = new StubAttachmentStore { ThrowOnWrite = true };

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.StorageUnavailable, result.Errors);
        Assert.NotNull(storage.DeletedKey);
        Assert.Null(metadata.Write);
    }

    [UnitFact]
    public async Task GivenMetadataFailureAfterWrite_WhenDocumentAttached_ThenObjectIsDeleted()
    {
        var metadata = new StubMetadataStore { ThrowOnCreate = true };
        var storage = new StubAttachmentStore();

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.PersistenceFailed, result.Errors);
        Assert.Equal(storage.WrittenKey, storage.DeletedKey);
    }

    [UnitFact]
    public async Task GivenTransactionDeletedDuringUpload_WhenMetadataSaved_ThenObjectIsDeleted()
    {
        var metadata = new StubMetadataStore
        {
            Outcome = AttachmentMetadataOutcome.TransactionNotFound
        };
        var storage = new StubAttachmentStore();

        var result = await Handler(metadata, storage).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(AttachmentMessages.TransactionNotFound, result.Errors);
        Assert.Equal(storage.WrittenKey, storage.DeletedKey);
    }

    [UnitTheory]
    [InlineData("too-large")]
    [InlineData("wrong-type")]
    [InlineData("missing")]
    public async Task GivenInvalidFile_WhenDocumentAttached_ThenNothingIsStored(string invalidity)
    {
        var command = Command();
        command.Content = invalidity switch
        {
            "too-large" => new byte[5],
            "missing" => [],
            _ => command.Content
        };
        command.ContentType = invalidity == "wrong-type" ? "text/plain" : command.ContentType;
        var metadata = new StubMetadataStore();
        var storage = new StubAttachmentStore();

        var result = await Handler(metadata, storage, maximumBytes: 3).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Null(storage.WrittenKey);
        Assert.Null(metadata.Write);
    }

    private static AttachDocumentCommandHandler Handler(
        StubMetadataStore metadata,
        StubAttachmentStore storage,
        int maximumBytes = 1024) => new(
        new AttachDocumentCommandValidator(new AttachmentOptions(
            maximumBytes,
            ["application/pdf", "image/png"])),
        new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
        new StubProfileReader(new UserProfileSnapshot(
            UserId, null, "Owner", "BRL", false, Now, Now)),
        metadata,
        storage,
        new FixedTimeProvider(),
        NullLogger<AttachDocumentCommandHandler>.Instance);

    private static AttachDocumentCommand Command() => new()
    {
        TransactionId = TransactionId,
        FileName = "receipt.pdf",
        ContentType = "application/pdf",
        Content = [1, 2, 3]
    };

    private sealed class StubMetadataStore : IAttachmentMetadataStore
    {
        public bool IsOwned { get; init; } = true;
        public bool ThrowOnCreate { get; init; }
        public AttachmentMetadataOutcome Outcome { get; init; } = AttachmentMetadataOutcome.Succeeded;
        public AttachmentMetadataWrite? Write { get; private set; }

        public Task<bool> IsOwnedLiveTransactionAsync(
            Guid userId, Guid transactionId, CancellationToken cancellationToken) =>
            Task.FromResult(IsOwned);

        public Task<AttachmentMetadataResult> CreateAsync(
            AttachmentMetadataWrite write,
            CancellationToken cancellationToken)
        {
            Write = write;
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("database unavailable");
            }

            var snapshot = Outcome == AttachmentMetadataOutcome.Succeeded
                ? new AttachmentSnapshot(
                    Guid.NewGuid(),
                    write.TransactionId,
                    write.FileName,
                    write.ContentType,
                    write.SizeInBytes,
                    write.CreatedAt)
                : null;
            return Task.FromResult(new AttachmentMetadataResult(Outcome, snapshot));
        }
    }

    private sealed class StubAttachmentStore : IAttachmentStore
    {
        public bool Healthy { get; init; } = true;
        public bool ThrowOnWrite { get; init; }
        public string? WrittenKey { get; private set; }
        public byte[]? WrittenContent { get; private set; }
        public string? DeletedKey { get; private set; }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Healthy);

        public async Task WriteAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken)
        {
            WrittenKey = key;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            WrittenContent = copy.ToArray();
            if (ThrowOnWrite)
            {
                throw new IOException("write failed");
            }
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            DeletedKey = key;
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
