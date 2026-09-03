using System.Text;
using ArturRios.Fortuna.Integration.Storage;
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
        await using var output = await store.OpenReadAsync("user/attachment.txt", CancellationToken.None);
        using var reader = new StreamReader(output);

        Assert.Equal("receipt", await reader.ReadToEndAsync(CancellationToken.None));
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

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            store.OpenReadAsync("attachment.txt", CancellationToken.None));
    }

    [UnitFact]
    public async Task GivenExistingStorageRoot_WhenHealthIsChecked_ThenItIsHealthy()
    {
        var store = new FilesystemAttachmentStore(root);

        Assert.True(await store.IsHealthyAsync(CancellationToken.None));
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

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
