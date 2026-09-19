using System.Text;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class FilesystemAttachmentStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"fortuna-storage-{Guid.NewGuid():N}");

    [UnitFact]
    public async Task GivenAttachment_WhenWrittenAndOpened_ThenContentRoundTripsExactly()
    {
        var store = new FilesystemAttachmentStore(root);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("receipt"));

        await store.WriteAsync("user/attachment.txt", input, CancellationToken.None);
        var result = await store.OpenReadAsync("user/attachment.txt", CancellationToken.None);

        Assert.True(result.IsFound);
        await using var output = result.Content;
        using var reader = new StreamReader(output);
        Assert.Equal("receipt", await reader.ReadToEndAsync(CancellationToken.None));
    }

    [UnitFact]
    public async Task GivenExistingObject_WhenOverwritten_ThenLatestContentIsStoredWithoutTemporaryFiles()
    {
        var store = new FilesystemAttachmentStore(root);
        await store.WriteAsync("user/attachment.txt", new MemoryStream([1]), CancellationToken.None);

        await store.WriteAsync("user/attachment.txt", new MemoryStream([2, 3]), CancellationToken.None);

        Assert.Equal([2, 3], await File.ReadAllBytesAsync(Path.Combine(root, "user", "attachment.txt")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "user")));
    }

    [UnitFact]
    public async Task GivenFailingSource_WhenWriting_ThenExistingObjectIsKeptAndNoTemporaryFileRemains()
    {
        var store = new FilesystemAttachmentStore(root);
        await store.WriteAsync("user/attachment.txt", new MemoryStream([1]), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() =>
            store.WriteAsync("user/attachment.txt", new FailingStream(), CancellationToken.None));

        Assert.Equal([1], await File.ReadAllBytesAsync(Path.Combine(root, "user", "attachment.txt")));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "user")));
    }

    [UnitFact]
    public async Task GivenMissingDirectory_WhenDeleted_ThenDeletionSucceeds()
    {
        var store = new FilesystemAttachmentStore(root);

        await store.DeleteAsync("missing-directory/attachment.txt", CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(root, "missing-directory")));
    }

    [UnitFact]
    public async Task GivenMissingObject_WhenOpened_ThenNotFoundIsReturned()
    {
        var store = new FilesystemAttachmentStore(root);

        var result = await store.OpenReadAsync("missing-directory/attachment.txt", CancellationToken.None);

        Assert.Equal(AttachmentReadStatus.NotFound, result.Status);
        Assert.Null(result.Content);
    }

    [UnitFact]
    public async Task GivenTraversalKey_WhenWriting_ThenInputIsRejected()
    {
        var store = new FilesystemAttachmentStore(root);
        await using var input = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.WriteAsync("../secret", input, CancellationToken.None));
    }

    [UnitFact]
    public async Task GivenStoredAttachment_WhenDeleted_ThenItCannotBeOpened()
    {
        var store = new FilesystemAttachmentStore(root);
        await store.WriteAsync("attachment.txt", new MemoryStream([1]), CancellationToken.None);

        await store.DeleteAsync("attachment.txt", CancellationToken.None);

        var result = await store.OpenReadAsync("attachment.txt", CancellationToken.None);

        Assert.Equal(AttachmentReadStatus.NotFound, result.Status);
    }

    [UnitFact]
    public async Task GivenExistingStorageRoot_WhenHealthIsChecked_ThenItIsHealthy()
    {
        var store = new FilesystemAttachmentStore(root);

        Assert.True(await store.IsHealthyAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [UnitFact]
    public async Task GivenRemovedStorageRoot_WhenHealthIsChecked_ThenItIsUnhealthy()
    {
        var store = new FilesystemAttachmentStore(root);
        Directory.Delete(root, recursive: true);

        Assert.False(await store.IsHealthyAsync(CancellationToken.None));
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData(" ")]
    public void GivenBlankRoot_WhenStoreIsCreated_ThenArgumentExceptionIsThrown(string invalidRoot)
    {
        Assert.Throws<ArgumentException>(() => new FilesystemAttachmentStore(invalidRoot));
    }

    [UnitFact]
    public async Task GivenAbsoluteKey_WhenWriting_ThenInputIsRejected()
    {
        var store = new FilesystemAttachmentStore(root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.WriteAsync(Path.GetFullPath("attachment.txt"), new MemoryStream([1]), CancellationToken.None));
    }

    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base([1, 2, 3])
        {
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("source failed"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
